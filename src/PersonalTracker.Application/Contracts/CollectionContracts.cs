namespace PersonalTracker.Application.Contracts;

public sealed record CoverInput(CoverType Type, string? Value);

/// <param name="Value">Gradient key or colour. Null for none/image.</param>
/// <param name="Version">For images: changes when the image changes (cache busting).</param>
public sealed record CoverDto(CoverType Type, string? Value, long? Version);

public sealed record FieldDto(
    Guid Id, Guid CollectionId, string Name, string Key, string? Description, FieldType Type,
    bool Required, int SortOrder, FieldConfig Config, AggregationType Aggregation, bool IsTitle, bool ShowInList);

public sealed record FieldInput(
    string Name, FieldType Type, bool Required, FieldConfig? Config,
    AggregationType Aggregation, bool IsTitle, bool ShowInList, string? Description);

public sealed record CollectionSummaryDto(
    Guid Id, string Name, string? Description, string Icon, CoverDto Cover,
    int RecordCount, int FieldCount, DateTime? LastActivityAt, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CollectionDetailDto(
    Guid Id, string Name, string? Description, string Icon, CoverDto Cover,
    int RecordCount, DateTime? LastActivityAt, DateTime CreatedAt, DateTime UpdatedAt,
    IReadOnlyList<FieldDto> Fields);

public sealed record CreateCollectionRequest(
    string Name, string? Description, string? Icon, CoverInput? Cover, List<FieldInput>? Fields);

public sealed record UpdateCollectionRequest(string? Name, string? Description, string? Icon);

public sealed record UpdateFieldRequest(
    string? Name, FieldType? Type, bool? Required, FieldConfig? Config, AggregationType? Aggregation,
    bool? IsTitle, bool? ShowInList, string? Description);

public sealed record ReorderFieldsRequest(List<Guid> FieldIds);

public sealed record FieldTypeInfoDto(
    FieldType Type, string Label, ValueKind Kind,
    IReadOnlyList<FilterOperator> Operators, IReadOnlyList<AggregationType> Aggregations,
    bool Sortable, IReadOnlyList<FieldType> ConvertibleTo);
