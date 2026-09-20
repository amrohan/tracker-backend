namespace PersonalTracker.Application.Abstractions;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, string? ip, CancellationToken ct);
    Task<AuthResult> LoginAsync(LoginRequest request, string? ip, CancellationToken ct);
    Task<AuthResult> RefreshAsync(string? refreshToken, string? ip, CancellationToken ct);
    Task LogoutAsync(string? refreshToken, CancellationToken ct);
    Task<UserDto> GetCurrentUserAsync(CancellationToken ct);
}

public interface ICollectionService
{
    Task<IReadOnlyList<CollectionSummaryDto>> ListAsync(CancellationToken ct);
    Task<CollectionDetailDto> GetAsync(Guid id, CancellationToken ct);
    Task<CollectionDetailDto> CreateAsync(CreateCollectionRequest request, CancellationToken ct);
    Task<CollectionDetailDto> UpdateAsync(Guid id, UpdateCollectionRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public interface IFieldService
{
    Task<FieldDto> AddAsync(Guid collectionId, FieldInput input, CancellationToken ct);
    Task<FieldDto> UpdateAsync(Guid fieldId, UpdateFieldRequest request, CancellationToken ct);
    Task DeleteAsync(Guid fieldId, CancellationToken ct);
    Task<IReadOnlyList<FieldDto>> ReorderAsync(Guid collectionId, ReorderFieldsRequest request, CancellationToken ct);
}

public interface ICoverService
{
    Task<CoverDto> SetAsync(Guid collectionId, CoverInput input, CancellationToken ct);
    Task<CoverDto> SetImageAsync(Guid collectionId, Stream content, long length, CancellationToken ct);
    Task<CoverImage> GetImageAsync(Guid collectionId, CancellationToken ct);
}

public interface IRecordService
{
    Task<RecordListResult> ListAsync(Guid collectionId, RecordQuery query, CancellationToken ct);
    Task<SummaryReportDto> SummarizeAsync(Guid collectionId, RecordQuery query, CancellationToken ct);
    Task<IReadOnlyList<LookupItem>> LookupAsync(Guid collectionId, string? search, int limit, CancellationToken ct);
    Task<CsvExport> ExportCsvAsync(Guid collectionId, RecordQuery query, CancellationToken ct);
    Task<RecordDetailResult> GetAsync(Guid id, CancellationToken ct);
    Task<RecordDetailResult> CreateAsync(Guid collectionId, SaveRecordRequest request, CancellationToken ct);
    Task<RecordDetailResult> UpdateAsync(Guid id, SaveRecordRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, bool force, CancellationToken ct);
}

/// <summary>Turns record ids into human readable labels using each target collection's title field.</summary>
public interface IReferenceResolver
{
    Task<Dictionary<Guid, RecordReferenceDto>> ResolveAsync(Guid userId, IEnumerable<Guid> ids, CancellationToken ct);
}

public interface IRecordValueValidator
{
    Task<Dictionary<string, object?>> ValidateAsync(
        Guid userId, IReadOnlyList<Field> fields,
        IReadOnlyDictionary<string, JsonElement> values,
        IReadOnlyDictionary<string, JsonElement>? existing,
        IEnumerable<string> submittedKeys, CancellationToken ct,
        bool verifyReferences = true);
}