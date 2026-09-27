using PersonalTracker.Infrastructure.D1;

namespace PersonalTracker.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(ID1Client db, IUnitOfWork uow) : IUserRepository
{
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var r = await db.QueryAsync("SELECT * FROM Users WHERE Id = ?", new object?[] { id.ToString() }, ct);
        return r.Rows.Count == 0 ? null : Map(r.Rows[0]);
    }

    public async Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct)
    {
        var r = await db.QueryAsync("SELECT * FROM Users WHERE NormalizedEmail = ?", new object?[] { normalizedEmail },
            ct);
        return r.Rows.Count == 0 ? null : Map(r.Rows[0]);
    }

    public async Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct)
    {
        var r = await db.QueryAsync("SELECT 1 FROM Users WHERE NormalizedEmail = ? LIMIT 1",
            new object?[] { normalizedEmail }, ct);
        return r.Rows.Count > 0;
    }

    public Task AddAsync(User user, CancellationToken ct)
    {
        uow.Enqueue(t => db.QueryAsync(
            "INSERT INTO Users (Id, Email, NormalizedEmail, DisplayName, PasswordHash, CreatedAt) VALUES (?, ?, ?, ?, ?, ?)",
            new object?[]
            {
                user.Id.ToString(), user.Email, user.NormalizedEmail, user.DisplayName, user.PasswordHash,
                D1Convert.ToSql(user.CreatedAt)
            },
            t));
        return Task.CompletedTask;
    }

    private static User Map(IReadOnlyDictionary<string, object?> row) => new()
    {
        Id = D1Convert.Guid(row, "Id"),
        Email = D1Convert.Str(row, "Email"),
        NormalizedEmail = D1Convert.Str(row, "NormalizedEmail"),
        DisplayName = D1Convert.Str(row, "DisplayName"),
        PasswordHash = D1Convert.Str(row, "PasswordHash"),
        CreatedAt = D1Convert.DateTime(row, "CreatedAt"),
    };
}