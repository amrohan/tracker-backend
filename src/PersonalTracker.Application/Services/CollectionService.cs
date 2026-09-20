namespace PersonalTracker.Application.Services;

public sealed class CollectionService(
    ICurrentUser user,
    ICollectionRepository collections,
    IFieldRepository fields,
    IRecordRepository records,
    IFileStorage storage,
    FieldBuilder fieldBuilder,
    IUnitOfWork uow,
    TimeProvider clock) : ICollectionService
{
    public async Task<IReadOnlyList<CollectionSummaryDto>> ListAsync(CancellationToken ct)
    {
        var userId = user.Id;
        var list = await collections.ListAsync(userId, ct);
        var stats = (await records.GetStatsAsync(userId, ct)).ToDictionary(s => s.CollectionId);
        return list
            .OrderByDescending(c => stats.TryGetValue(c.Id, out var s) && s.LastActivityAt > c.UpdatedAt ? s.LastActivityAt : c.UpdatedAt)
            .Select(c => CollectionMapper.ToSummary(c, stats.GetValueOrDefault(c.Id)))
            .ToList();
    }

    public async Task<CollectionDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var userId = user.Id;
        var collection = await collections.GetAsync(id, userId, includeFields: true, track: false, ct)
                         ?? throw new NotFoundException("Collection not found.");
        var stats = (await records.GetStatsAsync(userId, ct)).FirstOrDefault(s => s.CollectionId == id);
        return CollectionMapper.ToDetail(collection, stats);
    }

    public async Task<CollectionDetailDto> CreateAsync(CreateCollectionRequest request, CancellationToken ct)
    {
        var userId = user.Id;
        var now = clock.GetUtcNow().UtcDateTime;
        var errors = new ErrorBag();

        var name = Rules.Required(request.Name, 100, "name", "Name", errors);
        var description = Rules.Optional(request.Description, 1000, "description", "Description", errors);
        var icon = CleanIcon(request.Icon, errors);
        var cover = request.Cover ?? new CoverInput(CoverType.None, null);
        CoverRules.Validate(cover, errors);

        if (name.Length > 0 && await collections.NameExistsAsync(userId, name, null, ct))
            errors.Add("name", "You already have a collection with this name.");
        if (await collections.CountAsync(userId, ct) >= Limits.MaxCollectionsPerUser)
            errors.Add("name", $"You can have at most {Limits.MaxCollectionsPerUser} collections.");

        var inputs = request.Fields ?? new List<FieldInput>();
        if (inputs.Count > Limits.MaxFieldsPerCollection)
            errors.Add("fields", $"A collection can have at most {Limits.MaxFieldsPerCollection} fields.");
        if (inputs.Count(i => i.IsTitle) > 1)
            errors.Add("fields", "Only one field can be the title.");

        var collection = new Collection
        {
            UserId = userId,
            Name = name,
            Description = description,
            Icon = icon,
            CoverType = cover.Type,
            CoverValue = cover.Type == CoverType.None ? null : cover.Value,
            CoverUpdatedAt = cover.Type == CoverType.None ? null : now,
            CreatedAt = now,
            UpdatedAt = now
        };

        var existingCollectionIds = await collections.GetIdsAsync(userId, ct);
        var context = new ConfigContext(collection.Id, existingCollectionIds);
        var built = new List<Field>();
        for (var i = 0; i < inputs.Count && i < Limits.MaxFieldsPerCollection; i++)
            built.Add(fieldBuilder.Build(collection.Id, inputs[i], i, built, context, errors.Scope($"fields[{i}]."), now));

        errors.ThrowIfAny();

        foreach (var field in built) collection.Fields.Add(field);
        await collections.AddAsync(collection, ct);
        await uow.SaveChangesAsync(ct);
        return CollectionMapper.ToDetail(collection, null);
    }

    public async Task<CollectionDetailDto> UpdateAsync(Guid id, UpdateCollectionRequest request, CancellationToken ct)
    {
        var userId = user.Id;
        var collection = await collections.GetAsync(id, userId, includeFields: true, track: true, ct)
                         ?? throw new NotFoundException("Collection not found.");
        var errors = new ErrorBag();

        if (request.Name is not null)
        {
            var name = Rules.Required(request.Name, 100, "name", "Name", errors);
            if (name.Length > 0 && await collections.NameExistsAsync(userId, name, id, ct))
                errors.Add("name", "You already have a collection with this name.");
            collection.Name = name;
        }
        if (request.Description is not null)
            collection.Description = Rules.Optional(request.Description, 1000, "description", "Description", errors);
        if (request.Icon is not null)
            collection.Icon = CleanIcon(request.Icon, errors);
        errors.ThrowIfAny();

        collection.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await uow.SaveChangesAsync(ct);

        var stats = (await records.GetStatsAsync(userId, ct)).FirstOrDefault(s => s.CollectionId == id);
        return CollectionMapper.ToDetail(collection, stats);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var userId = user.Id;
        var collection = await collections.GetAsync(id, userId, includeFields: false, track: true, ct)
                         ?? throw new NotFoundException("Collection not found.");

        // Other collections that reference this one would be left with dangling fields.
        var dependents = (await fields.ListReferenceFieldsAsync(userId, ct))
            .Where(f => f.Config.TargetCollectionId == id && f.CollectionId != id)
            .ToList();
        if (dependents.Count > 0)
        {
            var names = (await collections.ListAsync(userId, ct))
                .Where(c => dependents.Any(d => d.CollectionId == c.Id))
                .Select(c => c.Name)
                .OrderBy(n => n)
                .ToList();
            throw new ConflictException(
                $"This collection is referenced by other collections ({string.Join(", ", names)}). Remove those reference fields first.",
                new Dictionary<string, object?> { ["dependents"] = names });
        }

        var imagePath = collection.CoverType == CoverType.Image ? collection.CoverValue : null;
        collections.Remove(collection);      // fields and records are removed by the FK cascade
        await uow.SaveChangesAsync(ct);
        if (imagePath is not null) await storage.DeleteAsync(imagePath, ct);
    }

    private static string CleanIcon(string? icon, ErrorBag errors)
    {
        var value = icon?.Trim();
        if (string.IsNullOrEmpty(value)) return "📁";
        if (value.Length > 16) errors.Add("icon", "Choose a single emoji.");
        return value;
    }
}
