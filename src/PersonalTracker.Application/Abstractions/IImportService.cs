namespace PersonalTracker.Application.Abstractions;

public interface IImportService
{
    Task<ImportResult> ImportAsync(Guid collectionId, ImportRequest request, CancellationToken ct);
}