using PersonalTracker.Infrastructure.D1;

namespace PersonalTracker.Infrastructure.Persistence.Repositories;

public sealed class RefreshTokenRepository(ID1Client db, IUnitOfWork uow) : IRefreshTokenRepository
{
    // AuthService loads a token then mutates RevokedAt/ReplacedByTokenHash directly on it before
    // calling SaveChangesAsync, so every row this repository returns must be tracked for update.
    public async Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct)
    {
        var r = await db.QueryAsync("SELECT * FROM RefreshTokens WHERE TokenHash = ?", new object?[] { tokenHash }, ct);
        if (r.Rows.Count == 0) return null;
        var token = Map(r.Rows[0]);
        Track(token);
        return token;
    }

    public async Task<List<RefreshToken>> GetActiveForUserAsync(Guid userId, DateTime now, CancellationToken ct)
    {
        var r = await db.QueryAsync(
            $"SELECT * FROM RefreshTokens WHERE UserId = ? AND RevokedAt IS NULL AND ExpiresAt > ?",
            [userId.ToString(), D1Convert.ToSql(now)], ct);
        var list = r.Rows.Select(Map).ToList();
        foreach (var t in list) Track(t);
        return list;
    }

    public Task AddAsync(RefreshToken token, CancellationToken ct)
    {
        uow.Enqueue(t => db.QueryAsync(
            """
            INSERT INTO RefreshTokens (Id, UserId, TokenHash, CreatedAt, ExpiresAt, RevokedAt, ReplacedByTokenHash, CreatedByIp)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            new object?[]
            {
                token.Id.ToString(), token.UserId.ToString(), token.TokenHash, D1Convert.ToSql(token.CreatedAt),
                D1Convert.ToSql(token.ExpiresAt),
                token.RevokedAt.HasValue ? D1Convert.ToSql(token.RevokedAt.Value) : null,
                token.ReplacedByTokenHash, token.CreatedByIp,
            }, t));
        return Task.CompletedTask;
    }

    public Task RemoveExpiredAsync(Guid userId, DateTime olderThan, CancellationToken ct)
    {
        uow.Enqueue(t => db.QueryAsync(
            "DELETE FROM RefreshTokens WHERE UserId = ? AND (ExpiresAt < ? OR (RevokedAt IS NOT NULL AND RevokedAt < ?))",
            new object?[] { userId.ToString(), D1Convert.ToSql(olderThan), D1Convert.ToSql(olderThan) }, t));
        return Task.CompletedTask;
    }

    private void Track(RefreshToken token) =>
        uow.Enqueue(t => db.QueryAsync(
            "UPDATE RefreshTokens SET RevokedAt = ?, ReplacedByTokenHash = ? WHERE Id = ?",
            new object?[]
            {
                token.RevokedAt.HasValue ? D1Convert.ToSql(token.RevokedAt.Value) : null, token.ReplacedByTokenHash,
                token.Id.ToString()
            },
            t));

    private static RefreshToken Map(IReadOnlyDictionary<string, object?> row) => new()
    {
        Id = D1Convert.Guid(row, "Id"),
        UserId = D1Convert.Guid(row, "UserId"),
        TokenHash = D1Convert.Str(row, "TokenHash"),
        CreatedAt = D1Convert.DateTime(row, "CreatedAt"),
        ExpiresAt = D1Convert.DateTime(row, "ExpiresAt"),
        RevokedAt = D1Convert.DateTimeOrNull(row, "RevokedAt"),
        ReplacedByTokenHash = D1Convert.StrOrNull(row, "ReplacedByTokenHash"),
        CreatedByIp = D1Convert.StrOrNull(row, "CreatedByIp"),
    };
}