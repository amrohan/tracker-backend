namespace PersonalTracker.Api.Endpoints;

public static class CollectionEndpoints
{
    public static void MapCollectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/collections").WithTags("Collections").RequireAuthorization();

        group.MapGet("/", (ICollectionService s, CancellationToken ct) => s.ListAsync(ct));

        group.MapGet("/{id:guid}", (Guid id, ICollectionService s, CancellationToken ct) => s.GetAsync(id, ct));

        group.MapPost("/", async (CreateCollectionRequest request, ICollectionService s, CancellationToken ct) =>
        {
            var created = await s.CreateAsync(request, ct);
            return Results.Created($"/api/collections/{created.Id}", created);
        });

        group.MapPatch("/{id:guid}",
            (Guid id, UpdateCollectionRequest request, ICollectionService s, CancellationToken ct) =>
                s.UpdateAsync(id, request, ct));

        group.MapDelete("/{id:guid}", async (Guid id, ICollectionService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        // ---- cover -------------------------------------------------------------------------
        group.MapPut("/{id:guid}/cover", (Guid id, CoverInput input, ICoverService s, CancellationToken ct) =>
            s.SetAsync(id, input, ct));

        group.MapDelete("/{id:guid}/cover", (Guid id, ICoverService s, CancellationToken ct) =>
            s.SetAsync(id, new CoverInput(Domain.Enums.CoverType.None, null), ct));

        group.MapPost("/{id:guid}/cover/image",
            async (Guid id, IFormFile file, ICoverService s, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return Results.Ok(await s.SetImageAsync(id, stream, file.Length, ct));
            }).DisableAntiforgery(); // bearer-token API: no cookie-based CSRF surface

        group.MapGet("/{id:guid}/cover/image",
            async (Guid id, ICoverService s, HttpContext http, CancellationToken ct) =>
            {
                var image = await s.GetImageAsync(id, ct);
                http.Response.Headers.CacheControl =
                    "private, max-age=31536000, immutable"; // URL carries a ?v= version
                return Results.File(image.Content, image.ContentType);
            });

        // ---- fields (nested under the collection) ------------------------------------------
        group.MapPost("/{id:guid}/fields", async (Guid id, FieldInput input, IFieldService s, CancellationToken ct) =>
        {
            var field = await s.AddAsync(id, input, ct);
            return Results.Created($"/api/fields/{field.Id}", field);
        });

        group.MapPut("/{id:guid}/fields/order",
            (Guid id, ReorderFieldsRequest request, IFieldService s, CancellationToken ct) =>
                s.ReorderAsync(id, request, ct));
    }
}