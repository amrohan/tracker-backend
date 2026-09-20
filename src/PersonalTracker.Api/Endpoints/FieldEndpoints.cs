namespace PersonalTracker.Api.Endpoints;

public static class FieldEndpoints
{
    public static void MapFieldEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/fields").WithTags("Fields").RequireAuthorization();

        group.MapPatch("/{id:guid}", (Guid id, UpdateFieldRequest request, IFieldService s, CancellationToken ct) =>
            s.UpdateAsync(id, request, ct));

        group.MapDelete("/{id:guid}", async (Guid id, IFieldService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });
    }
}
