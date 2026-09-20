namespace PersonalTracker.Application.Querying;

/// <summary>A record together with its parsed JSON values (parsed once per request).</summary>
public sealed class RecordRow(TrackerRecord entity, Dictionary<string, JsonElement> values)
{
    public TrackerRecord Entity { get; } = entity;
    public IReadOnlyDictionary<string, JsonElement> Values { get; } = values;

    /// <summary>The value for a field key, or null when absent/empty.</summary>
    public JsonElement? Get(string key)
    {
        if (Values.TryGetValue(key, out var value) && !JsonValues.IsEmpty(value)) return value;
        return null;
    }
}

public static class ReferenceIds
{
    public static IEnumerable<Guid> From(RecordRow row, IEnumerable<Field> referenceFields)
    {
        foreach (var field in referenceFields)
        {
            if (row.Get(field.Key) is not { } value) continue;
            foreach (var s in JsonValues.Strings(value))
                if (Guid.TryParse(s, out var id)) yield return id;
        }
    }
}
