using System.Globalization;
using System.Text.RegularExpressions;

namespace PersonalTracker.Application.Fields;

public sealed record CoerceContext(
    string? DateOrder,
    bool DecimalComma,
    TimeZoneInfo TimeZone,
    MissingPolicy MissingOption,
    MissingPolicy MissingReference,
    Func<Field, string, IReadOnlyList<Guid>> ResolveReference);

/// <summary>Converts imported text into the JSON value a field type expects. Blank text returns null.</summary>
public static partial class ImportCoercion
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}")]
    private static partial Regex IsoPrefix();

    [GeneratedRegex(@"^\s*(\d+(?:[.,]\d+)?)")]
    private static partial Regex LeadingNumber();

    private static readonly Dictionary<string, string[]> DateFormats = new()
    {
        ["dmy"] = ["d/M/yyyy", "d-M-yyyy", "d.M.yyyy", "d MMM yyyy", "d-MMM-yyyy", "d MMMM yyyy", "d/M/yy", "d-M-yy"],
        ["mdy"] = ["M/d/yyyy", "M-d-yyyy", "MMM d, yyyy", "MMMM d, yyyy", "M/d/yy"],
        ["ymd"] = ["yyyy/M/d", "yyyy.M.d", "yyyy-M-d"],
    };

    private static readonly string[] TimeSuffixes = ["", " H:mm", " H:mm:ss", " h:mm tt", " h:mm:ss tt"];

    private static readonly HashSet<string> Yes = new(StringComparer.OrdinalIgnoreCase)
        { "yes", "y", "true", "t", "1", "x", "✓", "on" };

    private static readonly HashSet<string> No = new(StringComparer.OrdinalIgnoreCase)
        { "no", "n", "false", "f", "0", "off" };

    public static JsonElement? Coerce(FieldType type, string raw, Field field, CoerceContext ctx, out string? error)
    {
        error = null;
        var s = raw.Trim();
        if (s.Length == 0) return null;

        switch (type)
        {
            case FieldType.Number:
            case FieldType.Currency: return ParseNumber(s, ctx, out error);
            case FieldType.Rating: return ParseRating(s, out error);
            case FieldType.Date: return ParseDate(s, ctx, out error);
            case FieldType.DateTime: return ParseDateTime(s, ctx, out error);
            case FieldType.Boolean: return ParseBool(s, out error);
            case FieldType.Select: return ParseSelect(s, field, ctx, false, out error);
            case FieldType.MultiSelect: return ParseSelect(s, field, ctx, true, out error);
            case FieldType.Reference: return ParseReference(s, field, ctx, false, out error);
            case FieldType.MultiReference: return ParseReference(s, field, ctx, true, out error);
            default: return Text(s); // text, long text, url: NormalizeValue validates length / scheme afterwards
        }
    }

    private static JsonElement Text(string value) => JsonSerializer.SerializeToElement(value);

    private static string[] Split(string s) =>
        s.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // "₹1,20,000"  "1.234,50" (decimal comma)  "(120)" = -120  "-5"
    private static JsonElement? ParseNumber(string s, CoerceContext ctx, out string? error)
    {
        error = null;
        var negative = s.StartsWith('-') || (s.StartsWith('(') && s.EndsWith(')'));
        var digits = new string(s.Where(c => char.IsAsciiDigit(c) || c is '.' or ',').ToArray());
        if (digits.Length == 0)
        {
            error = $"'{s}' is not a number.";
            return null;
        }

        digits = ctx.DecimalComma ? digits.Replace(".", "").Replace(',', '.') : digits.Replace(",", "");
        if (!decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
        {
            error = $"'{s}' is not a number. Check the decimal-comma setting.";
            return null;
        }

        return JsonSerializer.SerializeToElement(negative ? -d : d);
    }

    // "4", "4/5", "4.5" -> 5
    private static JsonElement? ParseRating(string s, out string? error)
    {
        error = null;
        var m = LeadingNumber().Match(s);
        if (!m.Success ||
            !decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture,
                out var stars))
        {
            error = $"'{s}' is not a rating.";
            return null;
        }

        return JsonSerializer.SerializeToElement((int)Math.Round(stars, MidpointRounding.AwayFromZero));
    }

    private static string[] Formats(string? order) =>
        DateFormats.GetValueOrDefault(order ?? "dmy") ?? DateFormats["dmy"];

    private static JsonElement? ParseDate(string s, CoerceContext ctx, out string? error)
    {
        error = null;
        if (IsoPrefix().IsMatch(s) &&
            DateOnly.TryParseExact(s[..10], JsonValues.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var iso))
            return Text(iso.ToString(JsonValues.DateFormat, CultureInfo.InvariantCulture));

        if (DateOnly.TryParseExact(s, Formats(ctx.DateOrder), CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var d))
            return Text(d.ToString(JsonValues.DateFormat, CultureInfo.InvariantCulture));

        error = $"'{s}' is not a date. Check the date order you chose.";
        return null;
    }

    // Text with an explicit offset / Z is honoured; text without one is read in the user's time zone.
    private static JsonElement? ParseDateTime(string s, CoerceContext ctx, out string? error)
    {
        error = null;
        DateTime parsed;
        if (IsoPrefix().IsMatch(s))
        {
            if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
            {
                error = $"'{s}' is not a date and time.";
                return null;
            }
        }
        else
        {
            var formats = Formats(ctx.DateOrder).SelectMany(f => TimeSuffixes.Select(t => f + t)).ToArray();
            if (!DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                error = $"'{s}' is not a date and time. Check the date order you chose.";
                return null;
            }
        }

        DateTime utc;
        switch (parsed.Kind)
        {
            case DateTimeKind.Utc: utc = parsed; break;
            case DateTimeKind.Local: utc = parsed.ToUniversalTime(); break;
            default:
                try
                {
                    utc = TimeZoneInfo.ConvertTimeToUtc(parsed, ctx.TimeZone);
                }
                catch (ArgumentException)
                {
                    error = $"'{s}' does not exist in your time zone (daylight-saving gap).";
                    return null;
                }

                break;
        }

        return Text(utc.ToString(JsonValues.InstantFormat, CultureInfo.InvariantCulture));
    }

    private static JsonElement? ParseBool(string s, out string? error)
    {
        error = null;
        if (Yes.Contains(s)) return JsonSerializer.SerializeToElement(true);
        if (No.Contains(s)) return JsonSerializer.SerializeToElement(false);
        error = $"'{s}' is not yes/no.";
        return null;
    }

    private static JsonElement? ParseSelect(string s, Field field, CoerceContext ctx, bool multiple, out string? error)
    {
        error = null;
        var result = new List<string>();
        foreach (var part in multiple ? Split(s) : new[] { s })
        {
            var options = field.Config.Options ??= new List<string>();
            var match = options.FirstOrDefault(o => string.Equals(o, part, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                switch (ctx.MissingOption)
                {
                    case MissingPolicy.Create when options.Count < 50 && part.Length <= 60:
                        options.Add(part); // saved only when the import is committed
                        match = part;
                        break;
                    case MissingPolicy.Skip:
                        continue;
                    default:
                        error = $"'{part}' is not one of the options for '{field.Name}'.";
                        return null;
                }
            }

            if (!result.Contains(match, StringComparer.OrdinalIgnoreCase)) result.Add(match);
        }

        if (result.Count == 0) return null;
        return multiple ? JsonSerializer.SerializeToElement(result) : JsonSerializer.SerializeToElement(result[0]);
    }

    private static JsonElement? ParseReference(string s, Field field, CoerceContext ctx, bool multiple,
        out string? error)
    {
        error = null;
        var ids = new List<string>();
        foreach (var label in multiple ? Split(s) : new[] { s })
        {
            var found = ctx.ResolveReference(field, label);
            if (found.Count == 0)
            {
                if (ctx.MissingReference == MissingPolicy.Skip) continue;
                error = $"No record named '{label}' was found for '{field.Name}'.";
                return null;
            }

            if (found.Count > 1)
            {
                error = $"'{label}' matches {found.Count} records for '{field.Name}'.";
                return null;
            }

            var id = found[0].ToString();
            if (!ids.Contains(id)) ids.Add(id);
        }

        if (ids.Count == 0) return null;
        return multiple ? JsonSerializer.SerializeToElement(ids) : JsonSerializer.SerializeToElement(ids[0]);
    }
}