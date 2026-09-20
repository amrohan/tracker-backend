namespace PersonalTracker.Application.Common;

public static class RecordData
{
    public static Dictionary<string, JsonElement> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, JsonElement>();
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonDefaults.Options)
               ?? new Dictionary<string, JsonElement>();
    }

    public static string Serialize(IDictionary<string, object?> values) =>
        JsonSerializer.Serialize(values, JsonDefaults.Options);

    public static string Serialize(IDictionary<string, JsonElement> values) =>
        JsonSerializer.Serialize(values, JsonDefaults.Options);
}
