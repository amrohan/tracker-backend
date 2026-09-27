using Amazon.S3;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PersonalTracker.Infrastructure.D1;
using PersonalTracker.Infrastructure.Persistence.Repositories;
using PersonalTracker.Infrastructure.Security;
using PersonalTracker.Infrastructure.Storage;

namespace PersonalTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // -------------------------
        // D1
        // -------------------------
        var d1 = configuration.GetSection("D1").Get<D1Options>() ?? new D1Options();

        if (string.IsNullOrWhiteSpace(d1.AccountId) ||
            string.IsNullOrWhiteSpace(d1.DatabaseId) ||
            string.IsNullOrWhiteSpace(d1.ApiToken))
        {
            throw new InvalidOperationException(
                "Configure D1:AccountId, D1:DatabaseId and D1:ApiToken " +
                "(env vars D1__AccountId, D1__DatabaseId, D1__ApiToken).");
        }

        services.AddSingleton(d1);

        services.AddHttpClient<ID1Client, HttpD1Client>(client =>
        {
            client.BaseAddress =
                new Uri("https://api.cloudflare.com/client/v4/");

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", d1.ApiToken);

            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<IUnitOfWork, D1UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<IFieldRepository, FieldRepository>();
        services.AddScoped<IRecordRepository, RecordRepository>();


        // -------------------------
        // JWT
        // -------------------------
        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>()
                  ?? new JwtOptions();

        if (string.IsNullOrWhiteSpace(jwt.SigningKey) ||
            jwt.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Configure Jwt:SigningKey with at least 32 characters " +
                "(env var Jwt__SigningKey).");
        }

        services.AddSingleton(jwt);


        // -------------------------
        // Cloudflare R2
        // -------------------------
        var r2 = configuration.GetSection("R2").Get<R2Options>()
                 ?? new R2Options();

        if (string.IsNullOrWhiteSpace(r2.AccountId) ||
            string.IsNullOrWhiteSpace(r2.AccessKeyId) ||
            string.IsNullOrWhiteSpace(r2.SecretAccessKey) ||
            string.IsNullOrWhiteSpace(r2.BucketName))
        {
            throw new InvalidOperationException(
                "Configure R2:AccountId, R2:AccessKeyId, " +
                "R2:SecretAccessKey and R2:BucketName.");
        }

        services.AddSingleton(r2);

        services.AddSingleton<IAmazonS3>(_ =>
        {
            var s3Config = new AmazonS3Config
            {
                ServiceURL =
                    $"https://{r2.AccountId}.r2.cloudflarestorage.com",

                AuthenticationRegion = "auto",
            };

            return new AmazonS3Client(
                r2.AccessKeyId,
                r2.SecretAccessKey,
                s3Config);
        });

        services.AddSingleton<IFileStorage, R2FileStorage>();


        // -------------------------
        // Other services
        // -------------------------
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        return services;
    }


    /// <summary>
    /// Applies the D1 schema.
    /// There are no EF migrations here — Schema.Statements is the single source of truth.
    /// </summary>
    public static async Task InitializeDatabaseAsync(
        this IServiceProvider services,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<ID1Client>();

        foreach (var statement in Schema.Statements)
        {
            await db.QueryAsync(statement, null, ct);
        }
    }
}