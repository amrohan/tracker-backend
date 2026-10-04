namespace PersonalTracker.Application.Services;

public sealed class FieldService(
    ICurrentUser user,
    ICollectionRepository collections,
    IFieldRepository fields,
    IRecordRepository records,
    IFieldTypeRegistry registry,
    FieldBuilder fieldBuilder,
    IUnitOfWork uow,
    TimeProvider clock) : IFieldService
{
    public async Task<FieldDto> AddAsync(Guid collectionId, FieldInput input, CancellationToken ct)
    {
        var userId = user.Id;
        _ = await collections.GetAsync(collectionId, userId, includeFields: false, track: false, ct)
            ?? throw new NotFoundException("Collection not found.");

        var siblings = await fields.ListByCollectionAsync(collectionId, track: true, ct);
        if (siblings.Count >= Limits.MaxFieldsPerCollection)
            throw new ConflictException($"A collection can have at most {Limits.MaxFieldsPerCollection} fields.");

        var errors = new ErrorBag();
        var context = new ConfigContext(collectionId, await collections.GetIdsAsync(userId, ct));
        var sortOrder = siblings.Count == 0 ? 0 : siblings.Max(f => f.SortOrder) + 1;
        var field = fieldBuilder.Build(collectionId, input, sortOrder, siblings, context, errors, clock.GetUtcNow().UtcDateTime);
        errors.ThrowIfAny();

        if (field.IsTitle)
            foreach (var sibling in siblings) sibling.IsTitle = false;

        await fields.AddAsync(field, ct);
        await uow.SaveChangesAsync(ct);
        return CollectionMapper.ToDto(field);
    }

    public async Task<FieldDto> UpdateAsync(Guid fieldId, UpdateFieldRequest request, CancellationToken ct)
    {
        var userId = user.Id;
        var field = await fields.GetAsync(fieldId, userId, ct) ?? throw new NotFoundException("Field not found.");
        var siblings = await fields.ListByCollectionAsync(field.CollectionId, track: true, ct);
        var errors = new ErrorBag();

        if (request.Name is not null)
        {
            var name = Rules.Required(request.Name, 80, "name", "Field name", errors);
            if (name.Length > 0 && siblings.Any(s => s.Id != field.Id && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                errors.Add("name", $"A field named '{name}' already exists.");
            field.Name = name; // the immutable Key is untouched, so stored values stay attached
        }
        if (request.Description is not null)
            field.Description = Rules.Optional(request.Description, 300, "description", "Description", errors);

        var oldType = field.Type;
        var newType = request.Type ?? oldType;
        var typeChanged = newType != oldType;
        if (typeChanged && (!Enum.IsDefined(newType) || !FieldTypeConversions.CanConvert(oldType, newType)))
        {
            errors.Add("type", $"A {registry.Get(oldType).Label} field cannot be changed to another type here. Add a new field instead.");
            errors.ThrowIfAny();
        }

        var handler = registry.Get(newType);
        List<(TrackerRecord Record, JsonDocument Doc)>? conversions = null;

        if (typeChanged || request.Config is not null)
        {
            var context = new ConfigContext(field.CollectionId, await collections.GetIdsAsync(userId, ct));
            var normalized = handler.NormalizeConfig(request.Config ?? field.Config, context, errors);

            if (!typeChanged && handler.IsReference && normalized.TargetCollectionId != field.Config.TargetCollectionId)
            {
                var existing = await records.ListByCollectionAsync(field.CollectionId, userId, track: false, ct);
                if (existing.Any(r => RecordData.Parse(r.Data).TryGetValue(field.Key, out var v) && !JsonValues.IsEmpty(v)))
                    errors.Add("config.targetCollectionId", "This reference already has values, so its target collection cannot change.");
            }

            field.Config = normalized;
            field.Type = newType;

            if (typeChanged)
                conversions = await ConvertExistingValuesAsync(field, handler, userId, errors, ct);
        }

        if (request.Aggregation is { } aggregation)
        {
            if (aggregation != AggregationType.None && !handler.Aggregations.Contains(aggregation))
                errors.Add("aggregation", $"{aggregation} is not available for {handler.Label} fields.");
            else
                field.Aggregation = aggregation;
        }
        else if (field.Aggregation != AggregationType.None && !handler.Aggregations.Contains(field.Aggregation))
        {
            field.Aggregation = AggregationType.None; // the old aggregation no longer applies to the new type
        }

        if (request.Required is { } required) field.Required = required && newType != FieldType.Boolean;
        if (request.ShowInList is { } showInList) field.ShowInList = showInList;
        if (request.IsTitle is { } isTitle)
        {
            if (isTitle)
                foreach (var sibling in siblings.Where(s => s.Id != field.Id)) sibling.IsTitle = false;
            field.IsTitle = isTitle;
        }

        errors.ThrowIfAny();

        if (conversions is not null)
            foreach (var (record, doc) in conversions)
            {
                var tracked = await records.GetAsync(record.Id, userId, track: true, ct);
                if (tracked is null) continue;
                tracked.Data = doc;
                tracked.Version++;
            }

        await uow.SaveChangesAsync(ct);
        return CollectionMapper.ToDto(field);
    }

    public async Task DeleteAsync(Guid fieldId, CancellationToken ct)
    {
        var userId = user.Id;
        var field = await fields.GetAsync(fieldId, userId, ct) ?? throw new NotFoundException("Field not found.");

        // Strip the field's values from every record so no orphaned data is left behind.
        var collectionRecords = await records.ListByCollectionAsync(field.CollectionId, userId, track: true, ct);
        foreach (var record in collectionRecords)
        {
            var data = RecordData.Parse(record.Data);
            if (!data.Remove(field.Key)) continue;
            record.Data = RecordData.ToDocument(data);
            record.Version++;
        }

        var remaining = (await fields.ListByCollectionAsync(field.CollectionId, track: true, ct))
            .Where(f => f.Id != field.Id)
            .OrderBy(f => f.SortOrder)
            .ToList();
        for (var i = 0; i < remaining.Count; i++) remaining[i].SortOrder = i;

        fields.Remove(field);
        await uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<FieldDto>> ReorderAsync(Guid collectionId, ReorderFieldsRequest request, CancellationToken ct)
    {
        var userId = user.Id;
        _ = await collections.GetAsync(collectionId, userId, includeFields: false, track: false, ct)
            ?? throw new NotFoundException("Collection not found.");

        var list = await fields.ListByCollectionAsync(collectionId, track: true, ct);
        var ids = request.FieldIds ?? new List<Guid>();
        if (ids.Count != list.Count || ids.Distinct().Count() != ids.Count || !list.All(f => ids.Contains(f.Id)))
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["fieldIds"] = ["Provide every field of the collection exactly once."]
            });

        for (var i = 0; i < ids.Count; i++)
            list.First(f => f.Id == ids[i]).SortOrder = i;

        await uow.SaveChangesAsync(ct);
        return list.OrderBy(f => f.SortOrder).Select(CollectionMapper.ToDto).ToList();
    }

    /// <summary>Re-validates stored values against the new type; returns the rewritten records (if any).</summary>
    private async Task<List<(TrackerRecord Record, JsonDocument Doc)>> ConvertExistingValuesAsync(
        Field field, IFieldTypeHandler handler, Guid userId, ErrorBag errors, CancellationToken ct)
    {
        var changes = new List<(TrackerRecord, JsonDocument)>();
        var invalid = 0;
        var existing = await records.ListByCollectionAsync(field.CollectionId, userId, track: false, ct);

        foreach (var record in existing)
        {
            var data = RecordData.Parse(record.Data);
            if (!data.TryGetValue(field.Key, out var value) || JsonValues.IsEmpty(value)) continue;

            var normalized = handler.NormalizeValue(value, field, value, out var error);
            if (error is not null || normalized is null) { invalid++; continue; }

            var rewritten = JsonSerializer.SerializeToElement(normalized, JsonDefaults.Options);
            if (rewritten.GetRawText() == value.GetRawText()) continue;
            data[field.Key] = rewritten;
            changes.Add((record, RecordData.ToDocument(data)));
        }

        if (invalid > 0)
            errors.Add("type", $"{invalid} record(s) contain values that do not fit the {handler.Label} type. Fix or clear them first.");
        return changes;
    }
}
