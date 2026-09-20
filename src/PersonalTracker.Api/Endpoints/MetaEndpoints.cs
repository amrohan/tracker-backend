using PersonalTracker.Application.Fields;

namespace PersonalTracker.Api.Endpoints;

public static class MetaEndpoints
{
    public static void MapMetaEndpoints(this IEndpointRouteBuilder app)
    {
        // Static catalog: which operators / aggregations / conversions each field type supports.
        app.MapGet("/api/meta/field-types", (IFieldTypeRegistry registry) => FieldTypeCatalog.Build(registry))
            .WithTags("Meta")
            .AllowAnonymous();
    }
}
