namespace PersonalTracker.Application.Common;

public static class Limits
{
    public const int MaxCollectionsPerUser = 200;
    public const int MaxFieldsPerCollection = 100;
    public const int MaxCoverBytes = 5 * 1024 * 1024;
    public const int MaxFilters = 20;
    public const int MaxPageSize = 200;
}
