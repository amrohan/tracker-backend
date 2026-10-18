namespace PersonalTracker.Application.Services;

public sealed class SavedFilterService(
    ICurrentUser user,
    ICollectionRepository collections,
    ISavedFilterRepository filters,
    IUnitOfWork uow,
    TimeProvider clock) : ISavedFilterService
{
    private const int MaxSavedFiltersPerCollection = 50;

    public async Task<IReadOnlyList<SavedFilterDto>> ListAsync(Guid collectionId, CancellationToken ct)
    {
        var userId = user.Id;
        await EnsureCollectionOwned(collectionId, userId, ct);
        var list = await filters.ListAsync(collectionId, userId, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<SavedFilterDto> CreateAsync(Guid collectionId, SavedFilterRequest request, CancellationToken ct)
    {
        var userId = user.Id;
        await EnsureCollectionOwned(collectionId, userId, ct);

        var errors = new ErrorBag();
        var name = Rules.Required(request.Name, 100, "name", "Name", errors);
        ValidateFiltersJson(request.FiltersJson, errors);
        errors.ThrowIfAny();

        var existing = await filters.ListAsync(collectionId, userId, ct);
        if (existing.Count >= MaxSavedFiltersPerCollection)
            throw new ConflictException($"You can save at most {MaxSavedFiltersPerCollection} filters per collection.");

        var now = clock.GetUtcNow().UtcDateTime;
        var filter = new SavedFilter
        {
            CollectionId = collectionId,
            UserId = userId,
            Name = name,
            FiltersJson = request.FiltersJson,
            CreatedAt = now,
            UpdatedAt = now
        };

        await filters.AddAsync(filter, ct);
        await uow.SaveChangesAsync(ct);
        return ToDto(filter);
    }

    public async Task<SavedFilterDto> UpdateAsync(Guid id, SavedFilterRequest request, CancellationToken ct)
    {
        var userId = user.Id;
        var filter = await filters.GetAsync(id, userId, ct)
                     ?? throw new NotFoundException("Saved filter not found.");

        var errors = new ErrorBag();
        var name = Rules.Required(request.Name, 100, "name", "Name", errors);
        ValidateFiltersJson(request.FiltersJson, errors);
        errors.ThrowIfAny();

        filter.Name = name;
        filter.FiltersJson = request.FiltersJson;
        filter.UpdatedAt = clock.GetUtcNow().UtcDateTime;

        await uow.SaveChangesAsync(ct);
        return ToDto(filter);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var userId = user.Id;
        var filter = await filters.GetAsync(id, userId, ct)
                     ?? throw new NotFoundException("Saved filter not found.");
        filters.Remove(filter);
        await uow.SaveChangesAsync(ct);
    }

    private async Task EnsureCollectionOwned(Guid collectionId, Guid userId, CancellationToken ct)
    {
        _ = await collections.GetAsync(collectionId, userId, includeFields: false, track: false, ct)
            ?? throw new NotFoundException("Collection not found.");
    }

    private static void ValidateFiltersJson(string json, ErrorBag errors)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            errors.Add("filtersJson", "Filters JSON is required.");
            return;
        }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                errors.Add("filtersJson", "Filters must be a JSON array.");
        }
        catch
        {
            errors.Add("filtersJson", "Invalid JSON for filters.");
        }
    }

    private static SavedFilterDto ToDto(SavedFilter f) =>
        new(f.Id, f.CollectionId, f.Name, f.FiltersJson, f.CreatedAt, f.UpdatedAt);
}
