namespace PersonalTracker.Application.Querying;

public static class AggregationCalculator
{
    public static IReadOnlyList<SummaryMetricDto> Compute(
        IReadOnlyList<Field> fields, IReadOnlyList<RecordRow> rows, IFieldTypeRegistry registry)
    {
        var metrics = new List<SummaryMetricDto>();
        foreach (var field in fields.Where(f => f.Aggregation != AggregationType.None).OrderBy(f => f.SortOrder))
        {
            var handler = registry.Get(field.Type);
            if (!handler.Aggregations.Contains(field.Aggregation)) continue;
            metrics.Add(new SummaryMetricDto(
                field.Id, field.Name, field.Type, field.Aggregation, field.Config.Currency,
                Calculate(field, handler.Kind, rows)));
        }
        return metrics;
    }

    private static object? Calculate(Field field, ValueKind kind, IReadOnlyList<RecordRow> rows)
    {
        var values = new List<JsonElement>();
        foreach (var row in rows)
            if (row.Get(field.Key) is { } v) values.Add(v);

        if (field.Aggregation == AggregationType.Count)
            return kind == ValueKind.Boolean ? values.Count(v => v.ValueKind == JsonValueKind.True) : values.Count;

        switch (kind)
        {
            case ValueKind.Number:
            {
                var numbers = new List<decimal>();
                foreach (var v in values)
                    if (JsonValues.TryGetDecimal(v, out var d)) numbers.Add(d);

                if (field.Aggregation == AggregationType.Sum) return numbers.Sum();
                if (numbers.Count == 0) return null;
                return field.Aggregation switch
                {
                    AggregationType.Average => (object)Math.Round(numbers.Average(), 4),
                    AggregationType.Min => numbers.Min(),
                    AggregationType.Max => numbers.Max(),
                    _ => null
                };
            }
            case ValueKind.Date:
            {
                var dates = new List<DateOnly>();
                foreach (var v in values)
                    if (JsonValues.TryGetDate(v, out var d)) dates.Add(d);
                if (dates.Count == 0) return null;
                var pick = field.Aggregation == AggregationType.Min ? dates.Min() : dates.Max();
                return pick.ToString(JsonValues.DateFormat, System.Globalization.CultureInfo.InvariantCulture);
            }
            case ValueKind.DateTime:
            {
                var instants = new List<DateTime>();
                foreach (var v in values)
                    if (JsonValues.TryGetInstant(v, out var d)) instants.Add(d);
                if (instants.Count == 0) return null;
                var pick = field.Aggregation == AggregationType.Min ? instants.Min() : instants.Max();
                return pick.ToString(JsonValues.InstantFormat, System.Globalization.CultureInfo.InvariantCulture);
            }
            default:
                return null;
        }
    }
}
