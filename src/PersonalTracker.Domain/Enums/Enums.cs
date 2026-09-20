namespace PersonalTracker.Domain.Enums;

public enum FieldType
{
    Text, LongText, Number, Currency, Date, DateTime, Boolean,
    Select, MultiSelect, Rating, Reference, MultiReference, Url
}

public enum AggregationType { None, Count, Sum, Average, Min, Max }

public enum CoverType { None, Gradient, Color, Image }

/// <summary>How a field's value behaves for filtering, sorting and aggregation.</summary>
public enum ValueKind { Text, Number, Date, DateTime, Boolean, Choice, MultiChoice }

public enum FilterOperator
{
    Contains, NotContains, Equal, NotEqual, StartsWith,
    GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual,
    Between, Before, After, IsEmpty, IsNotEmpty
}
