using PersonalTracker.Infrastructure.D1;

namespace PersonalTracker.Infrastructure.Persistence.Repositories;

public sealed class FieldRepository(ID1Client db, IUnitOfWork uow) : IFieldRepository
{
    public async Task<List<Field>> ListByCollectionAsync(Guid collectionId, bool track, CancellationToken ct)
    {
        var r = await db.QueryAsync("SELECT * FROM Fields WHERE CollectionId = ? ORDER BY SortOrder",
            new object?[] { collectionId.ToString() }, ct);
        var fields = r.Rows.Select(Map).ToList();
        if (track)
            foreach (var f in fields)
                TrackForUpdate(f);
        return fields;
    }

    public async Task<List<Field>> ListByCollectionsAsync(IReadOnlyCollection<Guid> collectionIds, CancellationToken ct)
    {
        var ids = collectionIds.ToList();
        if (ids.Count == 0) return new List<Field>();
        var placeholders = string.Join(",", ids.Select(_ => "?"));
        var args = ids.Select(id => (object?)id.ToString()).ToArray();
        var r = await db.QueryAsync($"SELECT * FROM Fields WHERE CollectionId IN ({placeholders})", args, ct);
        return r.Rows.Select(Map).ToList();
    }

    public async Task<Field?> GetAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var r = await db.QueryAsync(
            "SELECT f.* FROM Fields f JOIN Collections c ON c.Id = f.CollectionId WHERE f.Id = ? AND c.UserId = ?",
            new object?[] { id.ToString(), userId.ToString() }, ct);
        if (r.Rows.Count == 0) return null;
        var field = Map(r.Rows[0]);
        TrackForUpdate(field);
        return field;
    }

    public async Task<List<Field>> ListReferenceFieldsAsync(Guid userId, CancellationToken ct)
    {
        var r = await db.QueryAsync(
            """
            SELECT f.* FROM Fields f JOIN Collections c ON c.Id = f.CollectionId
            WHERE c.UserId = ? AND f.Type IN ('Reference', 'MultiReference')
            """,
            new object?[] { userId.ToString() }, ct);
        return r.Rows.Select(Map).ToList();
    }

    public Task AddAsync(Field field, CancellationToken ct)
    {
        uow.Enqueue(t => InsertAsync(db, field, t));
        return Task.CompletedTask;
    }

    public void Remove(Field field)
    {
        var id = field.Id;
        uow.Enqueue(t => db.QueryAsync("DELETE FROM Fields WHERE Id = ?", new object?[] { id.ToString() }, t));
    }

    private void TrackForUpdate(Field f) =>
        uow.Enqueue(t => db.QueryAsync(
            """
            UPDATE Fields SET Name = ?, Key = ?, Description = ?, Type = ?, Required = ?, SortOrder = ?, Config = ?,
                               Aggregation = ?, IsTitle = ?, ShowInList = ?
            WHERE Id = ?
            """,
            new object?[]
            {
                f.Name, f.Key, f.Description, f.Type.ToString(), f.Required ? 1 : 0, f.SortOrder,
                FieldConfigJson.Serialize(f.Config), f.Aggregation.ToString(), f.IsTitle ? 1 : 0, f.ShowInList ? 1 : 0,
                f.Id.ToString(),
            }, t));

    /// <summary>Shared with CollectionRepository, which inserts a new collection's initial fields itself.</summary>
    internal static Task InsertAsync(ID1Client db, Field f, CancellationToken ct) => db.QueryAsync(
        """
        INSERT INTO Fields (Id, CollectionId, Name, Key, Description, Type, Required, SortOrder, Config, Aggregation, IsTitle, ShowInList, CreatedAt)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        new object?[]
        {
            f.Id.ToString(), f.CollectionId.ToString(), f.Name, f.Key, f.Description, f.Type.ToString(),
            f.Required ? 1 : 0, f.SortOrder, FieldConfigJson.Serialize(f.Config), f.Aggregation.ToString(),
            f.IsTitle ? 1 : 0, f.ShowInList ? 1 : 0, D1Convert.ToSql(f.CreatedAt),
        }, ct);

    internal static Field Map(IReadOnlyDictionary<string, object?> row) => new()
    {
        Id = D1Convert.Guid(row, "Id"),
        CollectionId = D1Convert.Guid(row, "CollectionId"),
        Name = D1Convert.Str(row, "Name"),
        Key = D1Convert.Str(row, "Key"),
        Description = D1Convert.StrOrNull(row, "Description"),
        Type = Enum.Parse<FieldType>(D1Convert.Str(row, "Type")),
        Required = D1Convert.Bool(row, "Required"),
        SortOrder = D1Convert.Int(row, "SortOrder"),
        Config = FieldConfigJson.Deserialize(D1Convert.StrOrNull(row, "Config")),
        Aggregation = Enum.Parse<AggregationType>(D1Convert.Str(row, "Aggregation")),
        IsTitle = D1Convert.Bool(row, "IsTitle"),
        ShowInList = D1Convert.Bool(row, "ShowInList"),
        CreatedAt = D1Convert.DateTime(row, "CreatedAt"),
    };
}