using System.Text.RegularExpressions;

namespace PersonalTracker.Application.Services;

internal static partial class EmailRules
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")] private static partial Regex Pattern();
    public static bool IsValid(string email) => email.Length <= 254 && Pattern().IsMatch(email);
}

public sealed class AuthService(
    ICurrentUser currentUser,
    IUserRepository users,
    IRefreshTokenRepository tokens,
    IPasswordService passwords,
    ITokenService tokenService,
    IUnitOfWork uow,
    TimeProvider clock) : IAuthService
{
    /// <summary>Two tabs restoring a session at once both present the same cookie; the loser must not be logged out.</summary>
    private static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(10);
    private static string? _dummyHash;

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, string? ip, CancellationToken ct)
    {
        var errors = new ErrorBag();
        var email = (request.Email ?? string.Empty).Trim();
        if (email.Length == 0 || !EmailRules.IsValid(email)) errors.Add("email", "Enter a valid email address.");
        var displayName = Rules.Required(request.DisplayName, 100, "displayName", "Name", errors);
        var password = request.Password ?? string.Empty;
        if (password.Length < 8) errors.Add("password", "Use at least 8 characters.");
        if (password.Length > 128) errors.Add("password", "Use at most 128 characters.");
        errors.ThrowIfAny();

        var normalized = email.ToLowerInvariant();
        if (await users.EmailExistsAsync(normalized, ct))
            throw new ConflictException("An account with this email already exists.");

        var now = clock.GetUtcNow().UtcDateTime;
        var user = new User
        {
            Email = email,
            NormalizedEmail = normalized,
            DisplayName = displayName,
            PasswordHash = passwords.Hash(password),
            CreatedAt = now
        };
        await users.AddAsync(user, ct);
        var result = await IssueAsync(user, ip, now, ct);
        await uow.SaveChangesAsync(ct);
        return result;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, string? ip, CancellationToken ct)
    {
        var normalized = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await users.GetByNormalizedEmailAsync(normalized, ct);

        // Always verify a hash so response time does not reveal whether the account exists.
        var hash = user?.PasswordHash ?? (_dummyHash ??= passwords.Hash("timing-equaliser-password"));
        var valid = passwords.Verify(hash, request.Password ?? string.Empty);
        if (user is null || !valid) throw new AuthenticationFailedException("Invalid email or password.");

        var now = clock.GetUtcNow().UtcDateTime;
        await tokens.RemoveExpiredAsync(user.Id, now.AddDays(-7), ct);
        var result = await IssueAsync(user, ip, now, ct);
        await uow.SaveChangesAsync(ct);
        return result;
    }

    public async Task<AuthResult> RefreshAsync(string? refreshToken, string? ip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new AuthenticationFailedException("Session expired.");

        var now = clock.GetUtcNow().UtcDateTime;
        var stored = await tokens.GetByHashAsync(tokenService.HashRefreshToken(refreshToken), ct)
                     ?? throw new AuthenticationFailedException("Session expired.");

        if (stored.RevokedAt is { } revokedAt)
        {
            if (stored.ReplacedByTokenHash is not null && now - revokedAt <= ReuseGrace)
            {
                var replacement = await tokens.GetByHashAsync(stored.ReplacedByTokenHash, ct);
                var owner = await users.GetByIdAsync(stored.UserId, ct);
                if (replacement is { RevokedAt: null } && replacement.ExpiresAt > now && owner is not null)
                {
                    var access = tokenService.CreateAccessToken(owner);
                    // No new refresh token: the browser already holds the one issued by the winning tab.
                    return new AuthResult(new AuthResponse(access.Token, access.ExpiresAt, ToDto(owner)), null, null);
                }
            }

            // A rotated token was replayed outside the grace window: treat as theft and end every session.
            foreach (var active in await tokens.GetActiveForUserAsync(stored.UserId, now, ct)) active.RevokedAt = now;
            await uow.SaveChangesAsync(ct);
            throw new AuthenticationFailedException("Session expired.");
        }

        if (stored.ExpiresAt <= now) throw new AuthenticationFailedException("Session expired.");

        var user = await users.GetByIdAsync(stored.UserId, ct) ?? throw new AuthenticationFailedException("Session expired.");
        var result = await IssueAsync(user, ip, now, ct);
        stored.RevokedAt = now;
        stored.ReplacedByTokenHash = tokenService.HashRefreshToken(result.RefreshToken!);
        await uow.SaveChangesAsync(ct);
        return result;
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var stored = await tokens.GetByHashAsync(tokenService.HashRefreshToken(refreshToken), ct);
        if (stored is null || stored.RevokedAt is not null) return;
        stored.RevokedAt = clock.GetUtcNow().UtcDateTime;
        await uow.SaveChangesAsync(ct);
    }

    public async Task<UserDto> GetCurrentUserAsync(CancellationToken ct)
    {
        var user = await users.GetByIdAsync(currentUser.Id, ct) ?? throw new AuthenticationFailedException("Not authenticated.");
        return ToDto(user);
    }

    private async Task<AuthResult> IssueAsync(User user, string? ip, DateTime now, CancellationToken ct)
    {
        var access = tokenService.CreateAccessToken(user);
        var raw = tokenService.GenerateRefreshToken();
        var expires = now + tokenService.RefreshTokenLifetime;
        await tokens.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenService.HashRefreshToken(raw),
            CreatedAt = now,
            ExpiresAt = expires,
            CreatedByIp = ip
        }, ct);
        return new AuthResult(new AuthResponse(access.Token, access.ExpiresAt, ToDto(user)), raw, expires);
    }

    private static UserDto ToDto(User u) => new(u.Id, u.Email, u.DisplayName);
}
