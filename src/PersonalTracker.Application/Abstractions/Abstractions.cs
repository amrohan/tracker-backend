namespace PersonalTracker.Application.Abstractions;

public interface ICurrentUser
{
    /// <summary>Id of the authenticated user. Throws <see cref="AuthenticationFailedException"/> if anonymous.</summary>
    Guid Id { get; }
}

public interface IUnitOfWork
{
    /// <summary>Queues a write. Nothing touches the database until SaveChangesAsync flushes the queue, in order.</summary>
    void Enqueue(Func<CancellationToken, Task> write);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public sealed record AccessToken(string Token, DateTime ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);
    string GenerateRefreshToken();
    string HashRefreshToken(string token);
    TimeSpan RefreshTokenLifetime { get; }
}

public interface IFileStorage
{
    Task<string> SaveAsync(Guid userId, Stream content, string extension, CancellationToken ct);
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);
    Task DeleteAsync(string path, CancellationToken ct);
}