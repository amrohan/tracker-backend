using Microsoft.EntityFrameworkCore;

namespace PersonalTracker.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct);

    public async Task AddAsync(User user, CancellationToken ct) => await db.Users.AddAsync(user, ct);
}

public sealed class RefreshTokenRepository(AppDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct) =>
        db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public Task<List<RefreshToken>> GetActiveForUserAsync(Guid userId, DateTime now, CancellationToken ct) =>
        db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now).ToListAsync(ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct) => await db.RefreshTokens.AddAsync(token, ct);

    public async Task RemoveExpiredAsync(Guid userId, DateTime olderThan, CancellationToken ct)
    {
        var stale = await db.RefreshTokens
            .Where(t => t.UserId == userId && (t.ExpiresAt < olderThan || (t.RevokedAt != null && t.RevokedAt < olderThan)))
            .ToListAsync(ct);
        db.RefreshTokens.RemoveRange(stale);
    }
}

public sealed class CollectionRepository(AppDbContext db) : ICollectionRepository
{
    public Task<List<Collection>> ListAsync(Guid userId, CancellationToken ct) =>
        db.Collections.AsNoTracking().Include(c => c.Fields)
            .Where(c => c.UserId == userId).OrderBy(c => c.Name).ToListAsync(ct);

    public Task<Collection?> GetAsync(Guid id, Guid userId, bool includeFields, bool track, CancellationToken ct)
    {
        IQueryable<Collection> query = db.Collections;
        if (!track) query = query.AsNoTracking();
        if (includeFields) query = query.Include(c => c.Fields);
        return query.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
    }

    public Task<bool> NameExistsAsync(Guid userId, string name, Guid? excludeId, CancellationToken ct)
    {
        var lowered = name.ToLower();
        return db.Collections.AnyAsync(
            c => c.UserId == userId && c.Name.ToLower() == lowered && (excludeId == null || c.Id != excludeId), ct);
    }

    public Task<int> CountAsync(Guid userId, CancellationToken ct) =>
        db.Collections.CountAsync(c => c.UserId == userId, ct);

    public async Task<HashSet<Guid>> GetIdsAsync(Guid userId, CancellationToken ct) =>
        (await db.Collections.Where(c => c.UserId == userId).Select(c => c.Id).ToListAsync(ct)).ToHashSet();

    public async Task AddAsync(Collection collection, CancellationToken ct) => await db.Collections.AddAsync(collection, ct);

    public void Remove(Collection collection) => db.Collections.Remove(collection);
}

public sealed class FieldRepository(AppDbContext db) : IFieldRepository
{
    public Task<List<Field>> ListByCollectionAsync(Guid collectionId, bool track, CancellationToken ct)
    {
        IQueryable<Field> query = db.Fields;
        if (!track) query = query.AsNoTracking();
        return query.Where(f => f.CollectionId == collectionId).OrderBy(f => f.SortOrder).ToListAsync(ct);
    }

    public Task<List<Field>> ListByCollectionsAsync(IReadOnlyCollection<Guid> collectionIds, CancellationToken ct)
    {
        var ids = collectionIds.ToList();
        return db.Fields.AsNoTracking().Where(f => ids.Contains(f.CollectionId)).ToListAsync(ct);
    }

    public Task<Field?> GetAsync(Guid id, Guid userId, CancellationToken ct) =>
        db.Fields.FirstOrDefaultAsync(f => f.Id == id && f.Collection!.UserId == userId, ct);

    public Task<List<Field>> ListReferenceFieldsAsync(Guid userId, CancellationToken ct) =>
        db.Fields.AsNoTracking()
            .Where(f => f.Collection!.UserId == userId &&
                        (f.Type == FieldType.Reference || f.Type == FieldType.MultiReference))
            .ToListAsync(ct);

    public async Task AddAsync(Field field, CancellationToken ct) => await db.Fields.AddAsync(field, ct);

    public void Remove(Field field) => db.Fields.Remove(field);
}

public sealed class RecordRepository(AppDbContext db) : IRecordRepository
{
    private const int ChunkSize = 500; // stay far below SQLite's bound-parameter limit

    public Task<TrackerRecord?> GetAsync(Guid id, Guid userId, bool track, CancellationToken ct)
    {
        IQueryable<TrackerRecord> query = db.Records;
        if (!track) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
    }

    public Task<List<TrackerRecord>> ListByCollectionAsync(Guid collectionId, Guid userId, bool track, CancellationToken ct)
    {
        IQueryable<TrackerRecord> query = db.Records;
        if (!track) query = query.AsNoTracking();
        return query.Where(r => r.CollectionId == collectionId && r.UserId == userId).ToListAsync(ct);
    }

    public async Task<List<TrackerRecord>> GetByIdsAsync(Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var result = new List<TrackerRecord>();
        foreach (var chunk in ids.Distinct().Chunk(ChunkSize))
            result.AddRange(await db.Records.AsNoTracking()
                .Where(r => r.UserId == userId && chunk.Contains(r.Id)).ToListAsync(ct));
        return result;
    }

    public async Task<int> CountExistingAsync(Guid userId, Guid collectionId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var count = 0;
        foreach (var chunk in ids.Distinct().Chunk(ChunkSize))
            count += await db.Records.CountAsync(
                r => r.UserId == userId && r.CollectionId == collectionId && chunk.Contains(r.Id), ct);
        return count;
    }

    public async Task<IReadOnlyList<CollectionStats>> GetStatsAsync(Guid userId, CancellationToken ct)
    {
        var grouped = await db.Records.AsNoTracking()
            .Where(r => r.UserId == userId)
            .GroupBy(r => r.CollectionId)
            .Select(g => new { CollectionId = g.Key, Count = g.Count(), Last = g.Max(r => r.UpdatedAt) })
            .ToListAsync(ct);
        return grouped.Select(g => new CollectionStats(g.CollectionId, g.Count, g.Last)).ToList();
    }

    public async Task AddAsync(TrackerRecord record, CancellationToken ct) => await db.Records.AddAsync(record, ct);

    public void Remove(TrackerRecord record) => db.Records.Remove(record);
}
