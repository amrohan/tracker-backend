namespace PersonalTracker.Application.Services;

internal static class CollectionMapper
{
    public static FieldDto ToDto(Field f) => new(
        f.Id, f.CollectionId, f.Name, f.Key, f.Description, f.Type, f.Required, f.SortOrder,
        f.Config, f.Aggregation, f.IsTitle, f.ShowInList);

    public static CoverDto ToCoverDto(Collection c) => c.CoverType switch
    {
        CoverType.Gradient or CoverType.Color => new CoverDto(c.CoverType, c.CoverValue, null),
        CoverType.Image => new CoverDto(CoverType.Image, null, c.CoverUpdatedAt?.Ticks),
        _ => new CoverDto(CoverType.None, null, null)
    };

    public static CollectionSummaryDto ToSummary(Collection c, CollectionStats? stats) => new(
        c.Id, c.Name, c.Description, c.Icon, ToCoverDto(c),
        stats?.RecordCount ?? 0, c.Fields.Count, stats?.LastActivityAt, c.CreatedAt, c.UpdatedAt);

    public static CollectionDetailDto ToDetail(Collection c, CollectionStats? stats) => new(
        c.Id, c.Name, c.Description, c.Icon, ToCoverDto(c),
        stats?.RecordCount ?? 0, stats?.LastActivityAt, c.CreatedAt, c.UpdatedAt,
        c.Fields.OrderBy(f => f.SortOrder).Select(ToDto).ToList());
}
