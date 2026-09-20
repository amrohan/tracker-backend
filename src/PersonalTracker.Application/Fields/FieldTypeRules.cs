namespace PersonalTracker.Application.Fields;

public static class FilterOperators
{
    private static readonly FilterOperator[] TextOps =
    [
        FilterOperator.Contains, FilterOperator.NotContains, FilterOperator.Equal, FilterOperator.NotEqual,
        FilterOperator.StartsWith, FilterOperator.IsEmpty, FilterOperator.IsNotEmpty
    ];
    private static readonly FilterOperator[] NumberOps =
    [
        FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual,
        FilterOperator.LessThan, FilterOperator.LessThanOrEqual, FilterOperator.Between,
        FilterOperator.IsEmpty, FilterOperator.IsNotEmpty
    ];
    private static readonly FilterOperator[] DateOps =
    [
        FilterOperator.Equal, FilterOperator.Before, FilterOperator.After, FilterOperator.Between,
        FilterOperator.IsEmpty, FilterOperator.IsNotEmpty
    ];
    private static readonly FilterOperator[] DateTimeOps =
    [
        FilterOperator.Before, FilterOperator.After, FilterOperator.Between,
        FilterOperator.IsEmpty, FilterOperator.IsNotEmpty
    ];
    private static readonly FilterOperator[] BooleanOps = [FilterOperator.Equal, FilterOperator.NotEqual];
    private static readonly FilterOperator[] ChoiceOps =
        [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.IsEmpty, FilterOperator.IsNotEmpty];
    private static readonly FilterOperator[] MultiOps =
        [FilterOperator.Contains, FilterOperator.NotContains, FilterOperator.IsEmpty, FilterOperator.IsNotEmpty];

    public static IReadOnlyList<FilterOperator> For(ValueKind kind) => kind switch
    {
        ValueKind.Text => TextOps,
        ValueKind.Number => NumberOps,
        ValueKind.Date => DateOps,
        ValueKind.DateTime => DateTimeOps,
        ValueKind.Boolean => BooleanOps,
        ValueKind.Choice => ChoiceOps,
        ValueKind.MultiChoice => MultiOps,
        _ => []
    };

    /// <summary>Multi-value fields have no natural order.</summary>
    public static bool IsSortable(ValueKind kind) => kind != ValueKind.MultiChoice;
}

public static class FieldTypeConversions
{
    /// <summary>Type changes that never need a data migration beyond re-validation.</summary>
    public static IReadOnlyList<FieldType> TargetsFrom(FieldType from) => from switch
    {
        FieldType.Text => [FieldType.LongText, FieldType.Url],
        FieldType.LongText => [FieldType.Text, FieldType.Url],
        FieldType.Url => [FieldType.Text, FieldType.LongText],
        FieldType.Number => [FieldType.Currency],
        FieldType.Currency => [FieldType.Number],
        _ => []
    };

    public static bool CanConvert(FieldType from, FieldType to) => from == to || TargetsFrom(from).Contains(to);
}

public static class FieldTypeCatalog
{
    public static IReadOnlyList<FieldTypeInfoDto> Build(IFieldTypeRegistry registry) =>
        registry.All.Select(h => new FieldTypeInfoDto(
            h.Type, h.Label, h.Kind, FilterOperators.For(h.Kind), h.Aggregations,
            FilterOperators.IsSortable(h.Kind), FieldTypeConversions.TargetsFrom(h.Type))).ToList();
}

public static class TitleFieldPicker
{
    /// <summary>Explicit title field, else the first text field, else the first non-reference field, else the first field.</summary>
    public static Field? Pick(IReadOnlyList<Field> fields)
    {
        var ordered = fields.OrderBy(f => f.SortOrder).ToList();
        return ordered.FirstOrDefault(f => f.IsTitle)
               ?? ordered.FirstOrDefault(f => f.Type == FieldType.Text)
               ?? ordered.FirstOrDefault(f => f.Type is not (FieldType.Reference or FieldType.MultiReference))
               ?? ordered.FirstOrDefault();
    }
}
