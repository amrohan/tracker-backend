namespace PersonalTracker.Application.Services;

public sealed class RecordValueValidator(IFieldTypeRegistry registry, IRecordRepository records) : IRecordValueValidator
{
    public async Task<Dictionary<string, object?>> ValidateAsync(
        Guid userId, IReadOnlyList<Field> fields,
        IReadOnlyDictionary<string, JsonElement> values,
        IReadOnlyDictionary<string, JsonElement>? existing,
        IEnumerable<string> submittedKeys, CancellationToken ct, bool verifyReferences = true)
    {
        var errors = new ErrorBag();
        var known = fields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in submittedKeys)
            if (!known.Contains(key))
                errors.Add(key, "This field does not exist.");

        var result = new Dictionary<string, object?>();
        var referenceChecks = new List<(Field Field, List<Guid> Ids)>();

        foreach (var field in fields.OrderBy(f => f.SortOrder))
        {
            var handler = registry.Get(field.Type);

            if (!values.TryGetValue(field.Key, out var raw) || JsonValues.IsEmpty(raw))
            {
                if (field.Required) errors.Add(field.Key, $"{field.Name} is required.");
                continue;
            }

            JsonElement? stored = null;
            if (existing is not null && existing.TryGetValue(field.Key, out var storedValue)) stored = storedValue;

            var normalized = handler.NormalizeValue(raw, field, stored, out var error);
            if (error is not null)
            {
                errors.Add(field.Key, error);
                continue;
            }

            if (normalized is null)
            {
                if (field.Required) errors.Add(field.Key, $"{field.Name} is required.");
                continue;
            }

            result[field.Key] = normalized;
            if (handler.IsReference && field.Config.TargetCollectionId is not null)
                referenceChecks.Add((field, ToGuids(normalized)));
        }

        errors.ThrowIfAny();

        foreach (var (field, ids) in verifyReferences ? referenceChecks : new List<(Field Field, List<Guid> Ids)>())
        {
            var target = field.Config.TargetCollectionId!.Value;
            var distinct = ids.Distinct().ToList();
            var found = await records.CountExistingAsync(userId, target, distinct, ct);
            if (found != distinct.Count) errors.Add(field.Key, "One or more selected records no longer exist.");
        }

        errors.ThrowIfAny();

        return result;
    }

    private static List<Guid> ToGuids(object value)
    {
        var list = new List<Guid>();
        switch (value)
        {
            case string s when Guid.TryParse(s, out var g): list.Add(g); break;
            case List<string> many:
                foreach (var item in many)
                    if (Guid.TryParse(item, out var id))
                        list.Add(id);
                break;
        }

        return list;
    }
}