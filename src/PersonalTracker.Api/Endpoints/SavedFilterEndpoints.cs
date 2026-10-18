namespace PersonalTracker.Api.Endpoints;

public static class SavedFilterEndpoints
{
    public static void MapSavedFilterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/collections/{collectionId:guid}/saved-filters")
            .WithTags("SavedFilters")
            .RequireAuthorization();

        group.MapGet("/", (Guid collectionId, ISavedFilterService s, CancellationToken ct) =>
            s.ListAsync(collectionId, ct));

        group.MapPost("/", async (Guid collectionId, SavedFilterRequest request, ISavedFilterService s, CancellationToken ct) =>
        {
            var created = await s.CreateAsync(collectionId, request, ct);
            return Results.Created($"/api/collections/{collectionId}/saved-filters/{created.Id}", created);
        });

        group.MapPut("/{id:guid}", (Guid id, SavedFilterRequest request, ISavedFilterService s, CancellationToken ct) =>
            s.UpdateAsync(id, request, ct));

        group.MapDelete("/{id:guid}", async (Guid id, ISavedFilterService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });
    }
}
