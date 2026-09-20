using System.Globalization;

namespace PersonalTracker.Application.Fields;

public abstract class FieldTypeHandlerBase : IFieldTypeHandler
{
    private static readonly AggregationType[] CountOnly = [AggregationType.Count];

    public abstract FieldType Type { get; }
    public abstract string Label { get; }
    public abstract ValueKind Kind { get; }

    public virtual bool IsReference => false;
    public virtual bool IsSearchable => Kind is not (ValueKind.Boolean or ValueKind.DateTime);
    public virtual IReadOnlyList<AggregationType> Aggregations => CountOnly;

    public virtual FieldConfig NormalizeConfig(FieldConfig? input, ConfigContext context, ErrorBag errors) => new();

    public abstract object? NormalizeValue(JsonElement value, Field field, JsonElement? existing, out string? error);

    public virtual string? ToDisplayText(JsonElement value, Field field) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        JsonValueKind.Array => string.Join(", ", JsonValues.Strings(value)),
        _ => null
    };
}

// ---------------------------------------------------------------- text-like

public sealed class TextFieldHandler(FieldType type, string label, int maxLength) : FieldTypeHandlerBase
{
    public override FieldType Type => type;
    public override string Label => label;
    public override ValueKind Kind => ValueKind.Text;

    public override object? NormalizeValue(JsonElement value, Field field, JsonElement? existing, out string? error)
    {
        error = null;
        if (value.ValueKind != JsonValueKind.String) { error = "Must be text."; return null; }
        var s = value.GetString()!.Trim();
        if (s.Length == 0) return null;
        if (s.Length > maxLength) { error = $"Must be at most {maxLength} characters."; return null; }
        return s;
    }
}

public sealed class UrlFieldHandler : FieldTypeHandlerBase
{
    public override FieldType Type => FieldType.Url;
    public override string Label => "URL";
    public override ValueKind Kind => ValueKind.Text;

    public override object? NormalizeValue(JsonElement value, Field field, JsonElement? existing, out string? error)
    {
        error = null;
        if (value.ValueKind != JsonValueKind.String) { error = "Must be a URL."; return null; }
        var s = value.GetString()!.Trim();
        if (s.Length == 0) return null;
        if (s.Length > 2048) { error = "Must be at most 2048 characters."; return null; }
        // Only http/https: rejects javascript:, data:, file: etc. which would be XSS vectors when rendered as links.
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            error = "Enter a full web address starting with http:// or https://.";
            return null;
        }
        return s;
    }
}

// ---------------------------------------------------------------- numbers

public sealed class NumberFieldHandler(FieldType type, string label, bool isCurrency) : FieldTypeHandlerBase
{
    private const decimal MaxMagnitude = 1_000_000_000_000_000m;
    private static readonly AggregationType[] Numeric =
        [AggregationType.Count, AggregationType.Sum, AggregationType.Average, AggregationType.Min, AggregationType.Max];

    public override FieldType Type => type;
    public override string Label => label;
    public override ValueKind Kind => ValueKind.Number;
    public override IReadOnlyList<AggregationType> Aggregations => Numeric;

    public override FieldConfig NormalizeConfig(FieldConfig? input, ConfigContext context, ErrorBag errors)
    {
        var config = new FieldConfig { Min = input?.Min, Max = input?.Max };
        if (isCurrency)
        {
            var code = input?.Currency?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code)) code = "USD";
            if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
                errors.Add("config.currency", "Use a 3-letter currency code such as INR or USD.");
            config.Currency = code;
        }
        if (config.Min is not null && config.Max is not null && config.Min > config.Max)
            errors.Add("config.min", "Minimum cannot be greater than maximum.");
        return config;
    }

    public override object? NormalizeValue(JsonElement value, Field field, JsonElement? existing, out string? error)
    {
        error = null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var d))
        {
            error = "Must be a number.";
            return null;
        }
        if (Math.Abs(d) > MaxMagnitude) { error = "This number is too large."; return null; }
        if (isCurrency) d = decimal.Round(d, 2, MidpointRounding.AwayFromZero);
        if (field.Config.Min is { } min && d < min) { error = $"Must be at least {min.ToString(CultureInfo.InvariantCulture)}."; return null; }
        if (field.Config.Max is { } max && d > max) { error = $"Must be at most {max.ToString(CultureInfo.InvariantCulture)}."; return null; }
        return d;
    }
}

public sealed class RatingFieldHandler : FieldTypeHandlerBase
{
    private static readonly AggregationType[] Rating =
        [AggregationType.Count, AggregationType.Average, AggregationType.Min, AggregationType.Max];

    public override FieldType Type => FieldType.Rating;
    public override string Label => "Rating";
    public override ValueKind Kind => ValueKind.Number;
    public override IReadOnlyList<AggregationType> Aggregations => Rating;

    public override FieldConfig NormalizeConfig(FieldConfig? input, ConfigContext context, ErrorBag errors)
    {
        var max = input?.Max ?? 5m;
        if (max < 3 || max > 10 || max != decimal.Truncate(max))
        {
            errors.Add("config.max", "Rating scale must be a whole number between 3 and 10.");
            max = 5m;
        }
        return new FieldConfig { Max = max };
    }

    public override object? NormalizeValue(JsonElement value, Field field, JsonElement? existing, out string? error)
    {
        error = null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var n)) { error = "Must be a whole number."; return null; }
        if (n == 0) return null; // 0 means "no rating"
        var max = (int)(field.Config.Max ?? 5m);
        if (n < 1 || n > max) { error = $"Must be between 1 and {max}."; return null; }
        return (decimal)n;
    }
}

// ---------------------------------------------------------------- dates

public sealed class DateFieldHandler : FieldTypeHandlerBase
{
    private static readonly AggregationType[] Dates = [AggregationType.Count, AggregationType.Min, AggregationType.Max];

    public override FieldType Type => FieldType.Date;
    public override string Label => "Date";
    public override ValueKind Kind => ValueKind.Date;
    public override IReadOnlyList<AggregationType> Aggregations => Dates;

    public override object? NormalizeValue(JsonElement value, Field field, JsonElement? existing, out string? error)
    {
        error = null;
        if (!JsonValues.TryGetDate(value, out var date)) { error = "Use the format yyyy-MM-dd."; return null; }
        return date.ToString(JsonValues.DateFormat, CultureInfo.InvariantCulture);
    }
}

public sealed class DateTimeFieldHandler : FieldTypeHandlerBase
{
    private static readonly AggregationType[] Dates = [AggregationType.Count, AggregationType.Min, AggregationType.Max];

    public override FieldType Type => FieldType.DateTime;
    public override string Label => "Date & time";
    public override ValueKind Kind => ValueKind.DateTime;
    public override IReadOnlyList<AggregationType> Aggregations => Dates;

    public override object? NormalizeValue(JsonElement value, Field field, JsonElement? existing, out string? error)
    {
        error = null;
        if (!JsonValues.TryGetInstant(value, out var utc)) { error = "Enter a valid date and time."; return null; }
        return utc.ToString(JsonValues.InstantFormat, CultureInfo.InvariantCulture);
    }
}

// ---------------------------------------------------------------- boolean

public sealed class BooleanFieldHandler : FieldTypeHandlerBase
{
    public override FieldType Type => FieldType.Boolean;
    public override string Label => "Yes / No";
    public override ValueKind Kind => ValueKind.Boolean;

    public override object? NormalizeValue(JsonElement value, Field field, JsonElement? existing, out string? error)
    {
        error = null;
        switch (value.ValueKind)
        {
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            default: error = "Must be true or false."; return null;
        }
    }
}

// ---------------------------------------------------------------- select

public sealed class SelectFieldHandler(bool multiple) : FieldTypeHandlerBase
{
    public override FieldType Type => multiple ? FieldType.MultiSelect : FieldType.Select;
    public override string Label => multiple ? "Multi select" : "Select";
    public override ValueKind Kind => multiple ? ValueKind.MultiChoice : ValueKind.Choice;

    public override FieldConfig NormalizeConfig(FieldConfig? input, ConfigContext context, ErrorBag errors)
    {
        var options = (input?.Options ?? new List<string>())
            .Select(o => o?.Trim() ?? string.Empty)
            .Where(o => o.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.Count == 0) errors.Add("config.options", "Add at least one option.");
        if (options.Count > 50) errors.Add("config.options", "A field can have at most 50 options.");
        if (options.Any(o => o.Length > 60)) errors.Add("config.options", "Each option must be at most 60 characters.");
        return new FieldConfig { Options = options };
    }

    public override object? NormalizeValue(JsonElement value, Field field, JsonElement? existing, out string? error)
    {
        error = null;
        var options = field.Config.Options ?? new List<string>();
        // Leniency: a value that is already stored stays valid even if its option was later removed.
        var stored = existing.HasValue
            ? JsonValues.Strings(existing.Value).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? Canonical(string s) =>
            options.FirstOrDefault(o => string.Equals(o, s, StringComparison.OrdinalIgnoreCase))
            ?? (stored.Contains(s) ? s : null);

        if (!multiple)
        {
            if (value.ValueKind != JsonValueKind.String) { error = "Must be one of the options."; return null; }
            var s = value.GetString()!.Trim();
            if (s.Length == 0) return null;
            var canonical = Canonical(s);
            if (canonical is null) { error = $"'{s}' is not one of the options."; return null; }
            return canonical;
        }

        if (value.ValueKind != JsonValueKind.Array) { error = "Must be a list of options."; return null; }
        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) { error = "Every choice must be text."; return null; }
            var s = item.GetString()!.Trim();
            if (s.Length == 0) continue;
            var canonical = Canonical(s);
            if (canonical is null) { error = $"'{s}' is not one of the options."; return null; }
            if (!result.Contains(canonical, StringComparer.OrdinalIgnoreCase)) result.Add(canonical);
        }
        if (result.Count > 100) { error = "Too many choices selected."; return null; }
        return result.Count == 0 ? null : result;
    }
}

// ---------------------------------------------------------------- references

public sealed class ReferenceFieldHandler(bool multiple) : FieldTypeHandlerBase
{
    private static readonly AggregationType[] CountOnly = [AggregationType.Count];

    public override FieldType Type => multiple ? FieldType.MultiReference : FieldType.Reference;
    public override string Label => multiple ? "Multi reference" : "Reference";
    public override ValueKind Kind => multiple ? ValueKind.MultiChoice : ValueKind.Choice;
    public override bool IsReference => true;
    public override IReadOnlyList<AggregationType> Aggregations => CountOnly;

    public override FieldConfig NormalizeConfig(FieldConfig? input, ConfigContext context, ErrorBag errors)
    {
        var target = input?.TargetCollectionId;
        if (target is null || target == Guid.Empty)
            errors.Add("config.targetCollectionId", "Choose the collection to reference.");
        else if (target != context.CollectionId && !context.UserCollectionIds.Contains(target.Value))
            errors.Add("config.targetCollectionId", "The selected collection was not found.");
        return new FieldConfig { TargetCollectionId = target };
    }

    public override object? NormalizeValue(JsonElement value, Field field, JsonElement? existing, out string? error)
    {
        error = null;
        if (!multiple)
        {
            if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var id) || id == Guid.Empty)
            {
                error = "Must be a valid record.";
                return null;
            }
            return id.ToString();
        }

        if (value.ValueKind != JsonValueKind.Array) { error = "Must be a list of records."; return null; }
        var ids = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !Guid.TryParse(item.GetString(), out var id) || id == Guid.Empty)
            {
                error = "Every entry must be a valid record.";
                return null;
            }
            var s = id.ToString();
            if (!ids.Contains(s)) ids.Add(s);
        }
        if (ids.Count > 500) { error = "Too many records selected."; return null; }
        return ids.Count == 0 ? null : ids;
    }

    /// <summary>Raw ids are not human readable; labels are resolved separately.</summary>
    public override string? ToDisplayText(JsonElement value, Field field) => null;
}
