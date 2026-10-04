namespace PersonalTracker.Application.Services;

public sealed class ImportService(
    ICurrentUser user,
    ICollectionRepository collections,
    IFieldRepository fields,
    IRecordRepository records,
    IFieldTypeRegistry registry,
    IRecordValueValidator validator,
    IUnitOfWork uow,
    TimeProvider clock) : IImportService
{
    private const int MaxRows = 5000;
    private const int MaxReportedErrors = 200;

    private sealed class RefLookup(Guid targetCollectionId, Field? titleField, Dictionary<string, List<Guid>> index)
    {
        public Guid TargetCollectionId { get; } = targetCollectionId;
        public Field? TitleField { get; } = titleField;
        public Dictionary<string, List<Guid>> Index { get; } = index;
        public bool CanCreate => TitleField is not null && TitleField.Type is FieldType.Text or FieldType.LongText;
    }

    public async Task<ImportResult> ImportAsync(Guid collectionId, ImportRequest request, CancellationToken ct)
    {
        var userId = user.Id;
        _ = await collections.GetAsync(collectionId, userId, includeFields: false, track: false, ct)
            ?? throw new NotFoundException("Collection not found.");

        var rows = request.Rows;
        var mappings = request.Mappings;
        var options = request.Options;

        var collectionFields = await fields.ListByCollectionAsync(collectionId, track: true, ct);
        var byId = collectionFields.ToDictionary(f => f.Id);
        var byKey = collectionFields.ToDictionary(f => f.Key);

        // ---- guards --------------------------------------------------------------------------
        var guard = new ErrorBag();
        if (rows.Count == 0) guard.Add("rows", "The file has no data rows.");
        if (rows.Count > MaxRows) guard.Add("rows", $"Import at most {MaxRows} rows at a time.");
        if (mappings.Count == 0) guard.Add("mappings", "Map at least one column.");
        foreach (var m in mappings)
        {
            if (!byId.ContainsKey(m.FieldId)) guard.Add("mappings", "A mapping refers to a field that does not exist.");
            if (m.Column < 0) guard.Add("mappings", "A mapping refers to an invalid column.");
        }

        if (mappings.GroupBy(m => m.FieldId).Any(g => g.Count() > 1))
            guard.Add("mappings", "Each field can be mapped to only one column.");
        var mapped = mappings.Select(m => m.FieldId).ToHashSet();
        foreach (var required in collectionFields.Where(f =>
                     f.Required && f.Type != FieldType.Boolean && !mapped.Contains(f.Id)))
            guard.Add("mappings", $"'{required.Name}' is required, so a column must be mapped to it.");
        guard.ThrowIfAny();

        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone ?? "UTC");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            tz = TimeZoneInfo.Utc;
        }

        // ---- reference lookups: label -> ids of existing target records ------------------------
        var lookups = new Dictionary<Guid, RefLookup>();
        foreach (var m in mappings)
        {
            var field = byId[m.FieldId];
            if (!registry.Get(field.Type).IsReference || field.Config.TargetCollectionId is not { } targetId) continue;

            var targetFields = await fields.ListByCollectionAsync(targetId, track: false, ct);
            var title = TitleFieldPicker.Pick(targetFields);
            var index = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in await records.ListByCollectionAsync(targetId, userId, track: false, ct))
            {
                var label = RecordLabels.For(record, title, registry).Trim();
                if (!index.TryGetValue(label, out var list)) index[label] = list = new List<Guid>();
                list.Add(record.Id);
            }

            lookups[field.Id] = new RefLookup(targetId, title, index);
        }

        var cursor = clock.GetUtcNow().UtcDateTime;

        DateTime NextTimestamp()
        {
            var stamp = cursor;
            cursor = cursor.AddTicks(1);
            return stamp;
        }

        var accepted = new List<Dictionary<string, object?>>();
        var pending = new List<TrackerRecord>(); // target records created by "create missing"
        var rowCreated = new List<(RefLookup Lookup, string Label, TrackerRecord Record)>();
        var errors = new List<ImportRowError>();
        var errorCount = 0;

        void Report(int row, string fieldName, string message)
        {
            errorCount++;
            if (errors.Count < MaxReportedErrors) errors.Add(new ImportRowError(row, fieldName, message));
        }

        IReadOnlyList<Guid> Resolve(Field field, string label)
        {
            var lookup = lookups[field.Id];
            var key = label.Trim();
            if (lookup.Index.TryGetValue(key, out var ids)) return ids;

            if (options.MissingReference == MissingPolicy.Create && lookup.CanCreate && key.Length > 0)
            {
                var stamp = NextTimestamp();
                var created = new TrackerRecord
                {
                    CollectionId = lookup.TargetCollectionId,
                    UserId = userId,
                    DataJson = RecordData.Serialize(new Dictionary<string, object?> { [lookup.TitleField!.Key] = key }),
                    CreatedAt = stamp,
                    UpdatedAt = stamp
                };

                lookup.Index[key] = new List<Guid> { created.Id };
                rowCreated.Add((lookup, key, created));
                return new[] { created.Id };
            }

            return Array.Empty<Guid>();
        }

        // ---- rows ------------------------------------------------------------------------------
        for (var i = 0; i < rows.Count; i++)
        {
            var rowNumber = i + 1;
            var cells = rows[i] ?? new List<string?>();
            rowCreated.Clear();
            var values = new Dictionary<string, JsonElement>();
            var bad = false;

            foreach (var m in mappings)
            {
                var raw = m.Column < cells.Count ? cells[m.Column] : null;
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var field = byId[m.FieldId];
                var context = new CoerceContext(m.DateOrder, m.DecimalComma, tz,
                    options.MissingOption, options.MissingReference, Resolve);
                var element = ImportCoercion.Coerce(field.Type, raw, field, context, out var error);
                if (error is not null)
                {
                    Report(rowNumber, field.Name, error);
                    bad = true;
                    continue;
                }

                if (element is { } v) values[field.Key] = v;
            }

            if (!bad)
            {
                try
                {
                    accepted.Add(await validator.ValidateAsync(
                        userId, collectionFields, values, null, values.Keys.ToList(), ct, verifyReferences: false));
                    pending.AddRange(rowCreated.Select(c => c.Record));
                    continue;
                }
                catch (RequestValidationException ex)
                {
                    foreach (var (key, messages) in ex.Errors)
                        Report(rowNumber, byKey.TryGetValue(key, out var named) ? named.Name : key, messages[0]);
                }
            }

            foreach (var (lookup, label, _) in rowCreated) lookup.Index.Remove(label);
        }

        var blocked = errorCount > 0 && !options.SkipInvalidRows;
        if (request.DryRun || blocked || accepted.Count == 0)
            return new ImportResult(rows.Count, accepted.Count, 0, errorCount, errors, false);

        foreach (var record in pending) await records.AddAsync(record, ct);
        foreach (var data in accepted)
        {
            var stamp = NextTimestamp();
            await records.AddAsync(new TrackerRecord
            {
                CollectionId = collectionId,
                UserId = userId,
                DataJson = RecordData.Serialize(data),
                CreatedAt = stamp,
                UpdatedAt = stamp
            }, ct);
        }

        await uow.SaveChangesAsync(ct);

        return new ImportResult(rows.Count, accepted.Count, accepted.Count, errorCount, errors, true);
    }
}