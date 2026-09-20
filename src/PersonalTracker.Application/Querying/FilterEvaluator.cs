namespace PersonalTracker.Application.Querying;

/// <summary>Compiles a filter (field + operator + operands) into a predicate. Which operators apply is decided by the field's <see cref="ValueKind"/>.</summary>
public static class FilterEvaluator
{
    public static Func<RecordRow, bool> Compile(Field field, IFieldTypeHandler handler, RecordFilter filter)
    {
        var op = filter.Operator;
        if (!FilterOperators.For(handler.Kind).Contains(op))
            throw Invalid($"The operator '{op}' cannot be used with the field '{field.Name}'.");

        var key = field.Key;
        if (op == FilterOperator.IsEmpty) return r => r.Get(key) is null;
        if (op == FilterOperator.IsNotEmpty) return r => r.Get(key) is not null;

        switch (handler.Kind)
        {
            case ValueKind.Text: return CompileText(field, op, filter);
            case ValueKind.Number: return CompileNumber(field, op, filter);
            case ValueKind.Date: return CompileDate(field, op, filter);
            case ValueKind.DateTime: return CompileDateTime(field, op, filter);
            case ValueKind.Boolean: return CompileBoolean(field, op, filter);
            case ValueKind.Choice: return CompileChoice(field, op, filter);
            case ValueKind.MultiChoice: return CompileMulti(field, op, filter);
            default: throw Invalid($"Filtering is not supported for '{field.Name}'.");
        }
    }

    private static Func<RecordRow, bool> CompileText(Field field, FilterOperator op, RecordFilter f)
    {
        var key = field.Key;
        var needle = OperandString(f.Value, field);
        string? Get(RecordRow r) => r.Get(key) is { } e && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

        switch (op)
        {
            case FilterOperator.Contains:
                return r => Get(r)?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true;
            case FilterOperator.NotContains:
                return r => Get(r)?.Contains(needle, StringComparison.OrdinalIgnoreCase) != true;
            case FilterOperator.Equal: return r => string.Equals(Get(r), needle, StringComparison.OrdinalIgnoreCase);
            case FilterOperator.NotEqual:
                return r => !string.Equals(Get(r), needle, StringComparison.OrdinalIgnoreCase);
            case FilterOperator.StartsWith:
                return r => Get(r)?.StartsWith(needle, StringComparison.OrdinalIgnoreCase) == true;
            default: throw Invalid($"Unsupported operator '{op}'.");
        }
    }

    private static Func<RecordRow, bool> CompileNumber(Field field, FilterOperator op, RecordFilter f)
    {
        var key = field.Key;
        var a = OperandDecimal(f.Value, field);
        var b = op == FilterOperator.Between ? OperandDecimal(f.Value2, field) : 0m;
        var lo = Math.Min(a, b);
        var hi = Math.Max(a, b);

        decimal? Get(RecordRow r)
        {
            if (r.Get(key) is { } e && JsonValues.TryGetDecimal(e, out var d)) return d;
            return null;
        }

        switch (op)
        {
            case FilterOperator.Equal: return r => Get(r) == a;
            case FilterOperator.NotEqual: return r => Get(r) != a;
            case FilterOperator.GreaterThan: return r => Get(r) > a;
            case FilterOperator.GreaterThanOrEqual: return r => Get(r) >= a;
            case FilterOperator.LessThan: return r => Get(r) < a;
            case FilterOperator.LessThanOrEqual: return r => Get(r) <= a;
            case FilterOperator.Between: return r => Get(r) is { } v && v >= lo && v <= hi;
            default: throw Invalid($"Unsupported operator '{op}'.");
        }
    }

    private static Func<RecordRow, bool> CompileDate(Field field, FilterOperator op, RecordFilter f)
    {
        var key = field.Key;
        var a = OperandDate(f.Value, field);
        var b = op == FilterOperator.Between ? OperandDate(f.Value2, field) : a;
        var lo = a < b ? a : b;
        var hi = a < b ? b : a;

        DateOnly? Get(RecordRow r)
        {
            if (r.Get(key) is { } e && JsonValues.TryGetDate(e, out var d)) return d;
            return null;
        }

        switch (op)
        {
            case FilterOperator.Equal: return r => Get(r) == a;
            case FilterOperator.Before: return r => Get(r) is { } v && v < a;
            case FilterOperator.After: return r => Get(r) is { } v && v > a;
            case FilterOperator.Between: return r => Get(r) is { } v && v >= lo && v <= hi;
            default: throw Invalid($"Unsupported operator '{op}'.");
        }
    }

    private static Func<RecordRow, bool> CompileDateTime(Field field, FilterOperator op, RecordFilter f)
    {
        var key = field.Key;
        var a = OperandInstant(f.Value, field);
        var b = op == FilterOperator.Between ? OperandInstant(f.Value2, field) : a;
        var lo = a < b ? a : b;
        var hi = a < b ? b : a;

        DateTime? Get(RecordRow r)
        {
            if (r.Get(key) is { } e && JsonValues.TryGetInstant(e, out var d)) return d;
            return null;
        }

        switch (op)
        {
            case FilterOperator.Before: return r => Get(r) is { } v && v < a;
            case FilterOperator.After: return r => Get(r) is { } v && v > a;
            case FilterOperator.Between: return r => Get(r) is { } v && v >= lo && v <= hi;
            default: throw Invalid($"Unsupported operator '{op}'.");
        }
    }

    private static Func<RecordRow, bool> CompileBoolean(Field field, FilterOperator op, RecordFilter f)
    {
        var key = field.Key;
        if (f.Value is not { } operand || operand.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Invalid($"Choose yes or no for '{field.Name}'.");
        var expected = operand.ValueKind == JsonValueKind.True;
        bool Get(RecordRow r) => r.Get(key) is { ValueKind: JsonValueKind.True };

        return op == FilterOperator.Equal ? r => Get(r) == expected : r => Get(r) != expected;
    }

    private static Func<RecordRow, bool> CompileChoice(Field field, FilterOperator op, RecordFilter f)
    {
        var key = field.Key;
        var expected = OperandString(f.Value, field);
        string? Get(RecordRow r) => r.Get(key) is { } e && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

        return op == FilterOperator.Equal
            ? r => string.Equals(Get(r), expected, StringComparison.OrdinalIgnoreCase)
            : r => !string.Equals(Get(r), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static Func<RecordRow, bool> CompileMulti(Field field, FilterOperator op, RecordFilter f)
    {
        var key = field.Key;
        var expected = OperandString(f.Value, field);

        bool Has(RecordRow r) =>
            r.Get(key) is { } e && JsonValues.Strings(e)
                .Any(s => string.Equals(s, expected, StringComparison.OrdinalIgnoreCase));

        return op == FilterOperator.Contains ? r => Has(r) : r => !Has(r);
    }

    // ---- operands ---------------------------------------------------------------------------

    private static string OperandString(JsonElement? v, Field field)
    {
        if (v is { ValueKind: JsonValueKind.String } e && e.GetString() is { Length: > 0 } s) return s;
        throw Invalid($"Enter a value to filter '{field.Name}'.");
    }

    private static decimal OperandDecimal(JsonElement? v, Field field)
    {
        if (v is { } e && JsonValues.TryGetDecimal(e, out var d)) return d;
        throw Invalid($"Enter a number to filter '{field.Name}'.");
    }

    private static DateOnly OperandDate(JsonElement? v, Field field)
    {
        if (v is { } e && JsonValues.TryGetDate(e, out var d)) return d;
        throw Invalid($"Enter a date (yyyy-MM-dd) to filter '{field.Name}'.");
    }

    private static DateTime OperandInstant(JsonElement? v, Field field)
    {
        if (v is { } e && JsonValues.TryGetInstant(e, out var d)) return d;
        throw Invalid($"Enter a date and time to filter '{field.Name}'.");
    }

    public static RequestValidationException Invalid(string message) =>
        new(new Dictionary<string, string[]> { ["filters"] = [message] });
}