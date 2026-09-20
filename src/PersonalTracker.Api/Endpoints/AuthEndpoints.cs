namespace PersonalTracker.Api.Endpoints;

internal static class AuthCookie
{
    public const string Name = "pt_refresh";
    private const string CookiePath = "/api/auth";

    public static void Write(HttpContext http, AuthResult result)
    {
        if (result.RefreshToken is null || result.RefreshExpiresAt is null) return; // keep the cookie the browser already has

        http.Response.Cookies.Append(Name, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,                       // not readable from JavaScript
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Strict,        // never sent cross-site: CSRF protection for /refresh
            Path = CookiePath,                     // only sent to the auth endpoints
            IsEssential = true,
            Expires = new DateTimeOffset(DateTime.SpecifyKind(result.RefreshExpiresAt.Value, DateTimeKind.Utc))
        });
    }

    public static void Clear(HttpContext http) =>
        http.Response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath
        });
}

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest request, IAuthService auth, HttpContext http, CancellationToken ct) =>
        {
            var result = await auth.RegisterAsync(request, Ip(http), ct);
            AuthCookie.Write(http, result);
            return Results.Ok(result.Response);
        }).AllowAnonymous().RequireRateLimiting("auth");

        group.MapPost("/login", async (LoginRequest request, IAuthService auth, HttpContext http, CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(request, Ip(http), ct);
            AuthCookie.Write(http, result);
            return Results.Ok(result.Response);
        }).AllowAnonymous().RequireRateLimiting("auth");

        group.MapPost("/refresh", async (IAuthService auth, HttpContext http, CancellationToken ct) =>
        {
            try
            {
                var result = await auth.RefreshAsync(http.Request.Cookies[AuthCookie.Name], Ip(http), ct);
                AuthCookie.Write(http, result);
                return Results.Ok(result.Response);
            }
            catch (AuthenticationFailedException)
            {
                AuthCookie.Clear(http);
                throw;
            }
        }).AllowAnonymous();

        group.MapPost("/logout", async (IAuthService auth, HttpContext http, CancellationToken ct) =>
        {
            await auth.LogoutAsync(http.Request.Cookies[AuthCookie.Name], ct);
            AuthCookie.Clear(http);
            return Results.NoContent();
        }).AllowAnonymous();

        group.MapGet("/me", (IAuthService auth, CancellationToken ct) => auth.GetCurrentUserAsync(ct))
            .RequireAuthorization();
    }

    private static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();
}
