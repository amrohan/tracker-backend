namespace PersonalTracker.Application.Contracts;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record UserDto(Guid Id, string Email, string DisplayName);
public sealed record AuthResponse(string AccessToken, DateTime ExpiresAt, UserDto User);

/// <summary>Service result. <see cref="RefreshToken"/> is null when the existing cookie must be kept (multi-tab grace window).</summary>
public sealed record AuthResult(AuthResponse Response, string? RefreshToken, DateTime? RefreshExpiresAt);
