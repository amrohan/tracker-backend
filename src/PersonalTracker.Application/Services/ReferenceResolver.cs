namespace PersonalTracker.Application.Services;

public static class RecordLabels
{
    public static string For(TrackerRecord record, Field? titleField, IFieldTypeRegistry registry)
    {
        string? label = null;
        if (titleField is not null)
        {
            var data = RecordData.Parse(record.DataJson);
            if (data.TryGetValue(titleField.Key, out var value) && !JsonValues.IsEmpty(value))
                label = registry.Get(titleField.Type).ToDisplayText(value, titleField);
        }
        return string.IsNullOrWhiteSpace(label) ? "Untitled" : label;
    }
}

public sealed class ReferenceResolver(IRecordRepository records, IFieldRepository fields, IFieldTypeRegistry registry)
    : IReferenceResolver
{
    public async Task<Dictionary<Guid, RecordReferenceDto>> ResolveAsync(
        Guid userId, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, RecordReferenceDto>();
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0) return result;

        var found = await records.GetByIdsAsync(userId, distinct, ct);
        var collectionIds = found.Select(r => r.CollectionId).Distinct().ToList();
        var allFields = await fields.ListByCollectionsAsync(collectionIds, ct);
        var titles = allFields
            .GroupBy(f => f.CollectionId)
            .ToDictionary(g => g.Key, g => TitleFieldPicker.Pick(g.ToList()));

        foreach (var record in found)
        {
            titles.TryGetValue(record.CollectionId, out var titleField);
            result[record.Id] = new RecordReferenceDto(
                record.Id, record.CollectionId, RecordLabels.For(record, titleField, registry));
        }
        return result;
    }
}
