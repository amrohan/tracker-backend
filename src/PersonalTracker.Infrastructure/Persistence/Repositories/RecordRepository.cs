using PersonalTracker.Infrastructure.D1;

namespace PersonalTracker.Infrastructure.Persistence.Repositories;

public sealed class RecordRepository(ID1Client db, IUnitOfWork uow) : IRecordRepository
{
    private const int ChunkSize = 100; // keep well under D1's per-request parameter limits

    public async Task<TrackerRecord?> GetAsync(Guid id, Guid userId, bool track, CancellationToken ct)
    {
        var r = await db.QueryAsync("SELECT * FROM Records WHERE Id = ? AND UserId = ?",
            new object?[] { id.ToString(), userId.ToString() }, ct);
        if (r.Rows.Count == 0) return null;
        var record = Map(r.Rows[0]);
        if (track) TrackForUpdate(record, record.Version);
        return record;
    }

    public async Task<List<TrackerRecord>> ListByCollectionAsync(Guid collectionId, Guid userId, bool track,
        CancellationToken ct)
    {
        var r = await db.QueryAsync("SELECT * FROM Records WHERE CollectionId = ? AND UserId = ?",
            new object?[] { collectionId.ToString(), userId.ToString() }, ct);
        var records = r.Rows.Select(Map).ToList();
        if (track)
            foreach (var rec in records)
                TrackForUpdate(rec, rec.Version);
        return records;
    }

    public async Task<List<TrackerRecord>> GetByIdsAsync(Guid userId, IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        var result = new List<TrackerRecord>();
        foreach (var chunk in ids.Distinct().Chunk(ChunkSize))
        {
            var placeholders = string.Join(",", chunk.Select(_ => "?"));
            var args = new List<object?> { userId.ToString() };
            args.AddRange(chunk.Select(id => (object?)id.ToString()));
            var r = await db.QueryAsync($"SELECT * FROM Records WHERE UserId = ? AND Id IN ({placeholders})",
                args.ToArray(), ct);
            result.AddRange(r.Rows.Select(Map));
        }

        return result;
    }

    public async Task<int> CountExistingAsync(Guid userId, Guid collectionId, IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        var count = 0;
        foreach (var chunk in ids.Distinct().Chunk(ChunkSize))
        {
            var placeholders = string.Join(",", chunk.Select(_ => "?"));
            var args = new List<object?> { userId.ToString(), collectionId.ToString() };
            args.AddRange(chunk.Select(id => (object?)id.ToString()));
            var r = await db.QueryAsync(
                $"SELECT COUNT(*) AS N FROM Records WHERE UserId = ? AND CollectionId = ? AND Id IN ({placeholders})",
                args.ToArray(), ct);
            count += D1Convert.Int(r.Rows[0], "N");
        }

        return count;
    }

    public async Task<IReadOnlyList<CollectionStats>> GetStatsAsync(Guid userId, CancellationToken ct)
    {
        var r = await db.QueryAsync(
            "SELECT CollectionId, COUNT(*) AS Cnt, MAX(UpdatedAt) AS Last FROM Records WHERE UserId = ? GROUP BY CollectionId",
            new object?[] { userId.ToString() }, ct);
        return r.Rows.Select(row => new CollectionStats(
                D1Convert.Guid(row, "CollectionId"), D1Convert.Int(row, "Cnt"), D1Convert.DateTimeOrNull(row, "Last")))
            .ToList();
    }

    public Task AddAsync(TrackerRecord record, CancellationToken ct)
    {
        uow.Enqueue(t => db.QueryAsync(
            "INSERT INTO Records (Id, CollectionId, UserId, DataJson, CreatedAt, UpdatedAt, Version) VALUES (?, ?, ?, ?, ?, ?, ?)",
            new object?[]
            {
                record.Id.ToString(), record.CollectionId.ToString(), record.UserId.ToString(), record.DataJson,
                D1Convert.ToSql(record.CreatedAt), D1Convert.ToSql(record.UpdatedAt), record.Version,
            }, t));
        return Task.CompletedTask;
    }

    public void Remove(TrackerRecord record)
    {
        var id = record.Id;
        uow.Enqueue(t => db.QueryAsync("DELETE FROM Records WHERE Id = ?", new object?[] { id.ToString() }, t));
    }

    /// <summary>
    /// Replays EF Core's [ConcurrencyToken] behavior on Version: the write only lands if Version
    /// still matches what it was at load time; otherwise another request changed it first.
    /// </summary>
    private void TrackForUpdate(TrackerRecord record, long originalVersion) =>
        uow.Enqueue(async t =>
        {
            var result = await db.QueryAsync(
                "UPDATE Records SET DataJson = ?, UpdatedAt = ?, Version = ? WHERE Id = ? AND Version = ?",
                new object?[]
                {
                    record.DataJson, D1Convert.ToSql(record.UpdatedAt), record.Version, record.Id.ToString(),
                    originalVersion
                },
                t);
            if (result.Changes == 0)
                throw new ConflictException("This item was changed elsewhere. Reload it and try again.");
        });

    internal static TrackerRecord Map(IReadOnlyDictionary<string, object?> row) => new()
    {
        Id = D1Convert.Guid(row, "Id"),
        CollectionId = D1Convert.Guid(row, "CollectionId"),
        UserId = D1Convert.Guid(row, "UserId"),
        DataJson = D1Convert.Str(row, "DataJson"),
        CreatedAt = D1Convert.DateTime(row, "CreatedAt"),
        UpdatedAt = D1Convert.DateTime(row, "UpdatedAt"),
        Version = D1Convert.Long(row, "Version"),
    };
}