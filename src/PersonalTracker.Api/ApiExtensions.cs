using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PersonalTracker.Api.Endpoints;
using PersonalTracker.Api.Infrastructure;
using PersonalTracker.Infrastructure.Security;

namespace PersonalTracker.Api;

public static class ApiExtensions
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        services.ConfigureHttpJsonOptions(o => JsonDefaults.Configure(o.SerializerOptions));
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddOpenApi();
        services.AddHealthChecks();

        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name"
                };
            });
        services.AddAuthorization();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });

        return services;
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    public static WebApplication MapApi(this WebApplication app)
    {
        app.MapHealthChecks("/health").AllowAnonymous();
        if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();

        app.MapMetaEndpoints();
        app.MapAuthEndpoints();
        app.MapCollectionEndpoints();
        app.MapFieldEndpoints();
        app.MapRecordEndpoints();
        app.MapImportEndpoints();

        // Unknown /api routes are real 404s; every other unknown route is the Angular SPA.
        app.MapFallback("/api/{**path}",
                () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found"))
            .AllowAnonymous();
        app.MapFallbackToFile("index.html").AllowAnonymous();
        return app;
    }
}