using Microsoft.AspNetCore.Http.HttpResults;

namespace PersonalTracker.Api.Endpoints;

/// <summary>Query-string form of <see cref="RecordQuery"/>. <c>filters</c> is a JSON array (URL-encoded).</summary>
public sealed record RecordQueryParams(
    string? Search, string? SortBy, string? SortDir, int? Page, int? PageSize, string? Filters)
{
    public RecordQuery ToQuery()
    {
        var filters = new List<RecordFilter>();
        if (!string.IsNullOrWhiteSpace(Filters))
        {
            try
            {
                filters = JsonSerializer.Deserialize<List<RecordFilter>>(Filters, JsonDefaults.Options) ?? new List<RecordFilter>();
            }
            catch (JsonException)
            {
                throw new RequestValidationException(new Dictionary<string, string[]>
                {
                    ["filters"] = ["The filters could not be read."]
                });
            }
        }

        return new RecordQuery
        {
            Search = Search,
            Filters = filters,
            SortBy = SortBy,
            SortDirection = string.Equals(SortDir, "asc", StringComparison.OrdinalIgnoreCase) ? SortDirection.Asc : SortDirection.Desc,
            Page = Page ?? 1,
            PageSize = PageSize ?? 25
        };
    }
}

public static class RecordEndpoints
{
    public static void MapRecordEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Records").RequireAuthorization();

        group.MapGet("/collections/{collectionId:guid}/records",
            (Guid collectionId, [AsParameters] RecordQueryParams query, IRecordService s, CancellationToken ct) =>
                s.ListAsync(collectionId, query.ToQuery(), ct));

        group.MapGet("/collections/{collectionId:guid}/summary",
            (Guid collectionId, [AsParameters] RecordQueryParams query, IRecordService s, CancellationToken ct) =>
                s.SummarizeAsync(collectionId, query.ToQuery(), ct));

        group.MapGet("/collections/{collectionId:guid}/records/lookup",
            (Guid collectionId, string? q, int? limit, IRecordService s, CancellationToken ct) =>
                s.LookupAsync(collectionId, q, limit ?? 20, ct));

        group.MapGet("/collections/{collectionId:guid}/records/export",
            async (Guid collectionId, [AsParameters] RecordQueryParams query, IRecordService s, CancellationToken ct) =>
            {
                var export = await s.ExportCsvAsync(collectionId, query.ToQuery(), ct);
                return Results.File(export.Content, "text/csv; charset=utf-8", export.FileName);
            });

        group.MapPost("/collections/{collectionId:guid}/records",
            async (Guid collectionId, SaveRecordRequest request, IRecordService s, CancellationToken ct) =>
            {
                var created = await s.CreateAsync(collectionId, request, ct);
                return Results.Created($"/api/records/{created.Record.Id}", created);
            });

        group.MapGet("/records/{id:guid}", (Guid id, IRecordService s, CancellationToken ct) => s.GetAsync(id, ct));

        group.MapPatch("/records/{id:guid}",
            (Guid id, SaveRecordRequest request, IRecordService s, CancellationToken ct) => s.UpdateAsync(id, request, ct));

        group.MapDelete("/records/{id:guid}", async (Guid id, bool? force, IRecordService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, force ?? false, ct);
            return Results.NoContent();
        });
    }
}
