using System.Globalization;

namespace PersonalTracker.Application.Common;

public static class JsonValues
{
    public const string DateFormat = "yyyy-MM-dd";
    public const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static bool IsEmpty(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => true,
        JsonValueKind.String => string.IsNullOrWhiteSpace(e.GetString()),
        JsonValueKind.Array => e.GetArrayLength() == 0,
        _ => false
    };

    public static bool TryGetDecimal(JsonElement e, out decimal value)
    {
        value = 0;
        switch (e.ValueKind)
        {
            case JsonValueKind.Number:
                return e.TryGetDecimal(out value);
            case JsonValueKind.String:
                return decimal.TryParse(e.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
            default:
                return false;
        }
    }

    public static bool TryGetDate(JsonElement e, out DateOnly date)
    {
        date = default;
        return e.ValueKind == JsonValueKind.String &&
               DateOnly.TryParseExact(e.GetString(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>Parses an ISO-8601 instant (with or without offset; no offset is treated as UTC).</summary>
    public static bool TryGetInstant(JsonElement e, out DateTime utc)
    {
        utc = default;
        if (e.ValueKind != JsonValueKind.String) return false;
        if (!DateTimeOffset.TryParse(e.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)) return false;
        utc = dto.UtcDateTime;
        return true;
    }

    /// <summary>Yields the non-empty strings of a string or an array of strings.</summary>
    public static IEnumerable<string> Strings(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String)
        {
            var s = e.GetString();
            if (!string.IsNullOrEmpty(s)) yield return s;
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var s = item.GetString();
                if (!string.IsNullOrEmpty(s)) yield return s;
            }
        }
    }
}
