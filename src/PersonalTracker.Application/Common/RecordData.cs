using System.Text.Json;

namespace PersonalTracker.Application.Common;

public static class RecordData
{
    /// <summary>Parses the <see cref="JsonDocument"/> stored on a <c>TrackerRecord</c> into a mutable dictionary.</summary>
    public static Dictionary<string, JsonElement> Parse(JsonDocument? doc)
    {
        if (doc is null) return new Dictionary<string, JsonElement>();
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(doc.RootElement.GetRawText(), JsonDefaults.Options)
               ?? new Dictionary<string, JsonElement>();
    }

    /// <summary>Serializes a dictionary into a <see cref="JsonDocument"/> suitable for assigning to <c>TrackerRecord.Data</c>.</summary>
    public static JsonDocument ToDocument(IDictionary<string, object?> values) =>
        JsonDocument.Parse(JsonSerializer.Serialize(values, JsonDefaults.Options));

    /// <summary>Serializes a dictionary into a <see cref="JsonDocument"/> suitable for assigning to <c>TrackerRecord.Data</c>.</summary>
    public static JsonDocument ToDocument(IDictionary<string, JsonElement> values) =>
        JsonDocument.Parse(JsonSerializer.Serialize(values, JsonDefaults.Options));
}
