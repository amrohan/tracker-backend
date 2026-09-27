using PersonalTracker.Infrastructure.D1;

namespace PersonalTracker.Infrastructure.Persistence.Repositories;

public sealed class CollectionRepository(ID1Client db, IUnitOfWork uow) : ICollectionRepository
{
    public async Task<List<Collection>> ListAsync(Guid userId, CancellationToken ct)
    {
        var r = await db.QueryAsync("SELECT * FROM Collections WHERE UserId = ? ORDER BY Name",
            new object?[] { userId.ToString() }, ct);
        var collections = r.Rows.Select(Map).ToList();
        await AttachFieldsAsync(collections, ct);
        return collections;
    }

    public async Task<Collection?> GetAsync(Guid id, Guid userId, bool includeFields, bool track, CancellationToken ct)
    {
        var r = await db.QueryAsync("SELECT * FROM Collections WHERE Id = ? AND UserId = ?",
            new object?[] { id.ToString(), userId.ToString() }, ct);
        if (r.Rows.Count == 0) return null;
        var collection = Map(r.Rows[0]);
        if (includeFields) await AttachFieldsAsync(new[] { collection }, ct);
        if (track) TrackForUpdate(collection);
        return collection;
    }

    public async Task<bool> NameExistsAsync(Guid userId, string name, Guid? excludeId, CancellationToken ct)
    {
        var sql = "SELECT 1 FROM Collections WHERE UserId = ? AND LOWER(Name) = LOWER(?)" +
                  (excludeId is null ? "" : " AND Id <> ?") + " LIMIT 1";
        var args = excludeId is null
            ? new object?[] { userId.ToString(), name }
            : new object?[] { userId.ToString(), name, excludeId.Value.ToString() };
        var r = await db.QueryAsync(sql, args, ct);
        return r.Rows.Count > 0;
    }

    public async Task<int> CountAsync(Guid userId, CancellationToken ct)
    {
        var r = await db.QueryAsync("SELECT COUNT(*) AS N FROM Collections WHERE UserId = ?",
            new object?[] { userId.ToString() }, ct);
        return D1Convert.Int(r.Rows[0], "N");
    }

    public async Task<HashSet<Guid>> GetIdsAsync(Guid userId, CancellationToken ct)
    {
        var r = await db.QueryAsync("SELECT Id FROM Collections WHERE UserId = ?", new object?[] { userId.ToString() },
            ct);
        return r.Rows.Select(x => D1Convert.Guid(x, "Id")).ToHashSet();
    }

    public Task AddAsync(Collection collection, CancellationToken ct)
    {
        uow.Enqueue(t => db.QueryAsync(
            """
            INSERT INTO Collections (Id, UserId, Name, Description, Icon, CoverType, CoverValue, CoverUpdatedAt, CreatedAt, UpdatedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            new object?[]
            {
                collection.Id.ToString(), collection.UserId.ToString(), collection.Name, collection.Description,
                collection.Icon,
                collection.CoverType.ToString(), collection.CoverValue,
                collection.CoverUpdatedAt.HasValue ? D1Convert.ToSql(collection.CoverUpdatedAt.Value) : null,
                D1Convert.ToSql(collection.CreatedAt), D1Convert.ToSql(collection.UpdatedAt),
            }, t));

        // FieldBuilder attaches new fields straight onto collection.Fields (see CollectionService.CreateAsync)
        // rather than calling IFieldRepository — so this is the only place those rows get persisted.
        foreach (var field in collection.Fields)
        {
            var f = field;
            uow.Enqueue(t => FieldRepository.InsertAsync(db, f, t));
        }

        return Task.CompletedTask;
    }

    public void Remove(Collection collection)
    {
        var id = collection.Id;
        // Explicit cascade: a stateless HTTP call to D1 shouldn't be trusted to preserve
        // PRAGMA foreign_keys=ON from a previous request, so children are deleted by hand.
        uow.Enqueue(t =>
            db.QueryAsync("DELETE FROM Records WHERE CollectionId = ?", new object?[] { id.ToString() }, t));
        uow.Enqueue(t =>
            db.QueryAsync("DELETE FROM Fields WHERE CollectionId = ?", new object?[] { id.ToString() }, t));
        uow.Enqueue(t => db.QueryAsync("DELETE FROM Collections WHERE Id = ?", new object?[] { id.ToString() }, t));
    }

    private void TrackForUpdate(Collection c) =>
        uow.Enqueue(t => db.QueryAsync(
            """
            UPDATE Collections SET Name = ?, Description = ?, Icon = ?, CoverType = ?, CoverValue = ?, CoverUpdatedAt = ?, UpdatedAt = ?
            WHERE Id = ?
            """,
            new object?[]
            {
                c.Name, c.Description, c.Icon, c.CoverType.ToString(), c.CoverValue,
                c.CoverUpdatedAt.HasValue ? D1Convert.ToSql(c.CoverUpdatedAt.Value) : null,
                D1Convert.ToSql(c.UpdatedAt), c.Id.ToString(),
            }, t));

    private async Task AttachFieldsAsync(IReadOnlyList<Collection> collections, CancellationToken ct)
    {
        if (collections.Count == 0) return;
        var placeholders = string.Join(",", collections.Select(_ => "?"));
        var args = collections.Select(c => (object?)c.Id.ToString()).ToArray();
        var r = await db.QueryAsync($"SELECT * FROM Fields WHERE CollectionId IN ({placeholders}) ORDER BY SortOrder",
            args, ct);
        var byCollection = r.Rows.Select(FieldRepository.Map).GroupBy(f => f.CollectionId)
            .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var c in collections)
            c.Fields = byCollection.TryGetValue(c.Id, out var list) ? list : new List<Field>();
    }

    internal static Collection Map(IReadOnlyDictionary<string, object?> row) => new()
    {
        Id = D1Convert.Guid(row, "Id"),
        UserId = D1Convert.Guid(row, "UserId"),
        Name = D1Convert.Str(row, "Name"),
        Description = D1Convert.StrOrNull(row, "Description"),
        Icon = D1Convert.Str(row, "Icon"),
        CoverType = Enum.Parse<CoverType>(D1Convert.Str(row, "CoverType")),
        CoverValue = D1Convert.StrOrNull(row, "CoverValue"),
        CoverUpdatedAt = D1Convert.DateTimeOrNull(row, "CoverUpdatedAt"),
        CreatedAt = D1Convert.DateTime(row, "CreatedAt"),
        UpdatedAt = D1Convert.DateTime(row, "UpdatedAt"),
    };
}