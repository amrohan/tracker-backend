namespace PersonalTracker.Application.Abstractions;

public sealed record CollectionStats(Guid CollectionId, int RecordCount, DateTime? LastActivityAt);

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct);
    Task<List<RefreshToken>> GetActiveForUserAsync(Guid userId, DateTime now, CancellationToken ct);
    Task AddAsync(RefreshToken token, CancellationToken ct);
    Task RemoveExpiredAsync(Guid userId, DateTime olderThan, CancellationToken ct);
}

public interface ICollectionRepository
{
    Task<List<Collection>> ListAsync(Guid userId, CancellationToken ct);
    Task<Collection?> GetAsync(Guid id, Guid userId, bool includeFields, bool track, CancellationToken ct);
    Task<bool> NameExistsAsync(Guid userId, string name, Guid? excludeId, CancellationToken ct);
    Task<int> CountAsync(Guid userId, CancellationToken ct);
    Task<HashSet<Guid>> GetIdsAsync(Guid userId, CancellationToken ct);
    Task AddAsync(Collection collection, CancellationToken ct);
    void Remove(Collection collection);
}

public interface IFieldRepository
{
    Task<List<Field>> ListByCollectionAsync(Guid collectionId, bool track, CancellationToken ct);
    Task<List<Field>> ListByCollectionsAsync(IReadOnlyCollection<Guid> collectionIds, CancellationToken ct);
    Task<Field?> GetAsync(Guid id, Guid userId, CancellationToken ct);
    Task<List<Field>> ListReferenceFieldsAsync(Guid userId, CancellationToken ct);
    Task AddAsync(Field field, CancellationToken ct);
    void Remove(Field field);
}

public interface IRecordRepository
{
    Task<TrackerRecord?> GetAsync(Guid id, Guid userId, bool track, CancellationToken ct);
    Task<List<TrackerRecord>> ListByCollectionAsync(Guid collectionId, Guid userId, bool track, CancellationToken ct);
    Task<List<TrackerRecord>> GetByIdsAsync(Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<int> CountExistingAsync(Guid userId, Guid collectionId, IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<IReadOnlyList<CollectionStats>> GetStatsAsync(Guid userId, CancellationToken ct);
    Task AddAsync(TrackerRecord record, CancellationToken ct);
    void Remove(TrackerRecord record);
}
