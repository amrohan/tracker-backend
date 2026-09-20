namespace PersonalTracker.Application.Abstractions;

public interface ICurrentUser
{
    /// <summary>Id of the authenticated user. Throws <see cref="AuthenticationFailedException"/> if anonymous.</summary>
    Guid Id { get; }
}

public interface IUnitOfWork
{
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
    /// <summary>Stores the content and returns a relative path that can be passed to the other methods.</summary>
    Task<string> SaveAsync(Guid userId, Stream content, string extension, CancellationToken ct);
    Stream OpenRead(string path);
    Task DeleteAsync(string path, CancellationToken ct);
}
