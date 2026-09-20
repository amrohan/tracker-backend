using System.Text;

namespace PersonalTracker.Application.Services;

public sealed class RecordService(
    ICurrentUser user,
    ICollectionRepository collections,
    IFieldRepository fields,
    IRecordRepository records,
    IFieldTypeRegistry registry,
    IRecordQueryEngine engine,
    IRecordValueValidator validator,
    IReferenceResolver resolver,
    IUnitOfWork uow,
    TimeProvider clock) : IRecordService
{
    // ------------------------------------------------------------------ queries

    public async Task<RecordListResult> ListAsync(Guid collectionId, RecordQuery query, CancellationToken ct)
    {
        var userId = user.Id;
        var (_, collectionFields) = await LoadCollectionAsync(collectionId, ct);
        var all = await records.ListByCollectionAsync(collectionId, userId, track: false, ct);
        var rows = await engine.ExecuteAsync(userId, collectionFields, all, query, ct);

        var pageSize = Math.Clamp(query.PageSize, 1, Limits.MaxPageSize);
        var lastPage = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, lastPage);   // a page past the end returns the last page instead of nothing
        var items = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var references = await ResolveReferencesAsync(userId, items, collectionFields, ct);
        return new RecordListResult(items.Select(ToDto).ToList(), rows.Count, page, pageSize, references);
    }

    public async Task<SummaryReportDto> SummarizeAsync(Guid collectionId, RecordQuery query, CancellationToken ct)
    {
        var userId = user.Id;
        var (_, collectionFields) = await LoadCollectionAsync(collectionId, ct);
        var all = await records.ListByCollectionAsync(collectionId, userId, track: false, ct);
        var rows = await engine.ExecuteAsync(userId, collectionFields, all, query, ct);

        DateTime? lastActivity = rows.Count == 0 ? null : rows.Max(r => r.Entity.UpdatedAt);
        return new SummaryReportDto(rows.Count, lastActivity, AggregationCalculator.Compute(collectionFields, rows, registry));
    }

    public async Task<IReadOnlyList<LookupItem>> LookupAsync(Guid collectionId, string? search, int limit, CancellationToken ct)
    {
        var userId = user.Id;
        var (_, collectionFields) = await LoadCollectionAsync(collectionId, ct);
        var title = TitleFieldPicker.Pick(collectionFields);
        var all = await records.ListByCollectionAsync(collectionId, userId, track: false, ct);

        var items = all.Select(r => new LookupItem(r.Id, RecordLabels.For(r, title, registry)));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();
            items = items.Where(i => i.Label.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }
        return items.OrderBy(i => i.Label, StringComparer.OrdinalIgnoreCase).Take(Math.Clamp(limit, 1, 50)).ToList();
    }

    public async Task<CsvExport> ExportCsvAsync(Guid collectionId, RecordQuery query, CancellationToken ct)
    {
        var userId = user.Id;
        var (collection, collectionFields) = await LoadCollectionAsync(collectionId, ct);
        var all = await records.ListByCollectionAsync(collectionId, userId, track: false, ct);
        var rows = await engine.ExecuteAsync(userId, collectionFields, all, query, ct);
        var references = await ResolveReferencesAsync(userId, rows, collectionFields, ct);

        var ordered = collectionFields.OrderBy(f => f.SortOrder).ToList();
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", ordered.Select(f => CsvCell(f.Name, guardFormula: true))));

        foreach (var row in rows)
        {
            var cells = ordered.Select(field =>
            {
                var handler = registry.Get(field.Type);
                if (handler.Kind == ValueKind.Boolean)
                    return CsvCell(row.Get(field.Key) is { ValueKind: JsonValueKind.True } ? "Yes" : "No", false);
                if (row.Get(field.Key) is not { } value) return string.Empty;

                if (handler.IsReference)
                {
                    var labels = JsonValues.Strings(value)
                        .Select(s => Guid.TryParse(s, out var id) && references.TryGetValue(id, out var r) ? r.Label : string.Empty)
                        .Where(l => l.Length > 0);
                    return CsvCell(string.Join("; ", labels), guardFormula: true);
                }
                if (handler.Kind == ValueKind.MultiChoice)
                    return CsvCell(string.Join("; ", JsonValues.Strings(value)), guardFormula: true);

                var isText = handler.Kind is ValueKind.Text or ValueKind.Choice;
                return CsvCell(handler.ToDisplayText(value, field) ?? string.Empty, guardFormula: isText);
            });
            sb.AppendLine(string.Join(",", cells));
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true); // BOM so Excel detects UTF-8
        var content = encoding.GetPreamble().Concat(encoding.GetBytes(sb.ToString())).ToArray();
        return new CsvExport(SafeFileName(collection.Name) + ".csv", content);
    }

    public async Task<RecordDetailResult> GetAsync(Guid id, CancellationToken ct)
    {
        var userId = user.Id;
        var record = await records.GetAsync(id, userId, track: false, ct) ?? throw new NotFoundException("Record not found.");
        var collectionFields = await fields.ListByCollectionAsync(record.CollectionId, track: false, ct);
        return await BuildDetailAsync(userId, record, collectionFields, ct);
    }

    // ------------------------------------------------------------------ commands

    public async Task<RecordDetailResult> CreateAsync(Guid collectionId, SaveRecordRequest request, CancellationToken ct)
    {
        var userId = user.Id;
        var (_, collectionFields) = await LoadCollectionAsync(collectionId, ct);
        var values = request.Values ?? new Dictionary<string, JsonElement>();

        var normalized = await validator.ValidateAsync(userId, collectionFields, values, null, values.Keys, ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var record = new TrackerRecord
        {
            CollectionId = collectionId,
            UserId = userId,
            DataJson = RecordData.Serialize(normalized),
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1
        };
        await records.AddAsync(record, ct);
        await uow.SaveChangesAsync(ct);
        return await BuildDetailAsync(userId, record, collectionFields, ct);
    }

    public async Task<RecordDetailResult> UpdateAsync(Guid id, SaveRecordRequest request, CancellationToken ct)
    {
        var userId = user.Id;
        var record = await records.GetAsync(id, userId, track: true, ct) ?? throw new NotFoundException("Record not found.");

        if (request.Version is { } expected && expected != record.Version)
            throw new ConflictException("This record was changed elsewhere. Reload it and try again.");

        var collectionFields = await fields.ListByCollectionAsync(record.CollectionId, track: false, ct);
        var submitted = request.Values ?? new Dictionary<string, JsonElement>();
        var existing = RecordData.Parse(record.DataJson);

        // PATCH semantics: submitted keys overwrite, null/empty clears, everything else is kept.
        var merged = new Dictionary<string, JsonElement>(existing);
        foreach (var (key, value) in submitted)
        {
            if (JsonValues.IsEmpty(value)) merged.Remove(key);
            else merged[key] = value;
        }

        var normalized = await validator.ValidateAsync(userId, collectionFields, merged, existing, submitted.Keys, ct);

        record.DataJson = RecordData.Serialize(normalized);
        record.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        record.Version++;
        await uow.SaveChangesAsync(ct);
        return await BuildDetailAsync(userId, record, collectionFields, ct);
    }

    public async Task DeleteAsync(Guid id, bool force, CancellationToken ct)
    {
        var userId = user.Id;
        var record = await records.GetAsync(id, userId, track: true, ct) ?? throw new NotFoundException("Record not found.");

        var referencing = await FindReferencingRecordsAsync(userId, record, ct);
        if (referencing.Count > 0 && !force)
            throw new ConflictException(
                $"{referencing.Count} other record(s) refer to this one. Deleting it will clear those references.",
                new Dictionary<string, object?> { ["referenceCount"] = referencing.Count });

        foreach (var (other, data, otherFields) in referencing)
        {
            foreach (var field in otherFields) RemoveReference(data, field, record.Id);
            other.DataJson = RecordData.Serialize(data);
            other.Version++;
        }

        records.Remove(record);
        await uow.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<(Collection Collection, List<Field> Fields)> LoadCollectionAsync(Guid collectionId, CancellationToken ct)
    {
        var collection = await collections.GetAsync(collectionId, user.Id, includeFields: false, track: false, ct)
                         ?? throw new NotFoundException("Collection not found.");
        var collectionFields = await fields.ListByCollectionAsync(collectionId, track: false, ct);
        return (collection, collectionFields);
    }

    private async Task<RecordDetailResult> BuildDetailAsync(
        Guid userId, TrackerRecord record, IReadOnlyList<Field> collectionFields, CancellationToken ct)
    {
        var row = new RecordRow(record, RecordData.Parse(record.DataJson));
        var references = await ResolveReferencesAsync(userId, new[] { row }, collectionFields, ct);
        return new RecordDetailResult(ToDto(row), references);
    }

    private async Task<Dictionary<Guid, RecordReferenceDto>> ResolveReferencesAsync(
        Guid userId, IEnumerable<RecordRow> rows, IReadOnlyList<Field> collectionFields, CancellationToken ct)
    {
        var referenceFields = collectionFields.Where(f => registry.Get(f.Type).IsReference).ToList();
        if (referenceFields.Count == 0) return new Dictionary<Guid, RecordReferenceDto>();
        var ids = rows.SelectMany(r => ReferenceIds.From(r, referenceFields)).Distinct().ToList();
        return await resolver.ResolveAsync(userId, ids, ct);
    }

    private static RecordDto ToDto(RecordRow row) => new(
        row.Entity.Id, row.Entity.CollectionId, row.Values, row.Entity.CreatedAt, row.Entity.UpdatedAt, row.Entity.Version);

    private async Task<List<(TrackerRecord Record, Dictionary<string, JsonElement> Data, List<Field> Fields)>>
        FindReferencingRecordsAsync(Guid userId, TrackerRecord target, CancellationToken ct)
    {
        var result = new List<(TrackerRecord, Dictionary<string, JsonElement>, List<Field>)>();
        var referenceFields = (await fields.ListReferenceFieldsAsync(userId, ct))
            .Where(f => f.Config.TargetCollectionId == target.CollectionId)
            .ToList();

        foreach (var group in referenceFields.GroupBy(f => f.CollectionId))
        {
            var candidates = await records.ListByCollectionAsync(group.Key, userId, track: true, ct);
            foreach (var candidate in candidates)
            {
                if (candidate.Id == target.Id) continue;
                var data = RecordData.Parse(candidate.DataJson);
                var hits = group.Where(f => data.TryGetValue(f.Key, out var v) && ContainsId(v, target.Id)).ToList();
                if (hits.Count > 0) result.Add((candidate, data, hits));
            }
        }
        return result;
    }

    private static bool ContainsId(JsonElement value, Guid id) =>
        JsonValues.Strings(value).Any(s => Guid.TryParse(s, out var g) && g == id);

    private static void RemoveReference(Dictionary<string, JsonElement> data, Field field, Guid id)
    {
        if (!data.TryGetValue(field.Key, out var value)) return;
        var remaining = JsonValues.Strings(value).Where(s => !(Guid.TryParse(s, out var g) && g == id)).ToList();
        if (remaining.Count == 0 || field.Type == FieldType.Reference) data.Remove(field.Key);
        else data[field.Key] = JsonSerializer.SerializeToElement(remaining, JsonDefaults.Options);
    }

    private static string CsvCell(string value, bool guardFormula)
    {
        // Spreadsheet formula injection: cells starting with these characters are executed by Excel/Sheets.
        if (guardFormula && value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
        if (value.IndexOfAny([',', '"', '\n', '\r']) >= 0)
            value = "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return cleaned.Length == 0 ? "export" : cleaned;
    }
}
