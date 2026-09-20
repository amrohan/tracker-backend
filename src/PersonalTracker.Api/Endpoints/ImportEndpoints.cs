namespace PersonalTracker.Api.Endpoints;

public static class ImportEndpoints
{
    public static void MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/collections/{collectionId:guid}/import",
                (Guid collectionId, ImportRequest request, IImportService service, CancellationToken ct) =>
                    service.ImportAsync(collectionId, request, ct))
            .WithTags("Import")
            .RequireAuthorization();
    }
}