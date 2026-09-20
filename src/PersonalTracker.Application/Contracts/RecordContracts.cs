namespace PersonalTracker.Application.Contracts;

public sealed record RecordDto(
    Guid Id, Guid CollectionId, IReadOnlyDictionary<string, JsonElement> Values,
    DateTime CreatedAt, DateTime UpdatedAt, long Version);

public sealed record RecordReferenceDto(Guid Id, Guid CollectionId, string Label);

public sealed record RecordListResult(
    IReadOnlyList<RecordDto> Items, int Total, int Page, int PageSize,
    IReadOnlyDictionary<Guid, RecordReferenceDto> References);

public sealed record RecordDetailResult(RecordDto Record, IReadOnlyDictionary<Guid, RecordReferenceDto> References);

public sealed record SaveRecordRequest(Dictionary<string, JsonElement> Values, long? Version);

public sealed record RecordFilter(Guid FieldId, FilterOperator Operator, JsonElement? Value, JsonElement? Value2);

public enum SortDirection { Asc, Desc }

public sealed class RecordQuery
{
    public string? Search { get; init; }
    public List<RecordFilter> Filters { get; init; } = new();
    /// <summary>A field id, "createdAt" or "updatedAt". Null = newest first.</summary>
    public string? SortBy { get; init; }
    public SortDirection SortDirection { get; init; } = SortDirection.Desc;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed record LookupItem(Guid Id, string Label);

public sealed record SummaryMetricDto(
    Guid FieldId, string FieldName, FieldType FieldType, AggregationType Aggregation, string? Currency, object? Value);

public sealed record SummaryReportDto(int RecordCount, DateTime? LastActivityAt, IReadOnlyList<SummaryMetricDto> Metrics);

public sealed record CoverImage(Stream Content, string ContentType);

public sealed record CsvExport(string FileName, byte[] Content);
