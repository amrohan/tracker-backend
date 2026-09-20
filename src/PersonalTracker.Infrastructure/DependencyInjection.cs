using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PersonalTracker.Infrastructure.Persistence;
using PersonalTracker.Infrastructure.Persistence.Repositories;
using PersonalTracker.Infrastructure.Security;
using PersonalTracker.Infrastructure.Storage;

namespace PersonalTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // The provider is chosen here only; nothing outside Infrastructure knows about SQLite.
        // To move to PostgreSQL: add Npgsql.EntityFrameworkCore.PostgreSQL and call UseNpgsql here.
        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=App_Data/tracker.db";
        EnsureSqliteDirectory(connectionString);
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<IFieldRepository, FieldRepository>();
        services.AddScoped<IRecordRepository, RecordRepository>();

        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
            throw new InvalidOperationException(
                "Configure Jwt:SigningKey with at least 32 characters (environment variable Jwt__SigningKey).");
        services.AddSingleton(jwt);

        var storage = configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
        services.AddSingleton(storage);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        return services;
    }

    /// <summary>Applies migrations when the assembly has any, otherwise creates the schema directly.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (db.Database.GetMigrations().Any()) await db.Database.MigrateAsync(ct);
        else await db.Database.EnsureCreatedAsync(ct);

        try
        {
            // WAL lets reads continue while a write is in progress.
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
        }
        catch (Exception)
        {
            // non-fatal
        }
    }

    private static void EnsureSqliteDirectory(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:") return;
        var directory = Path.GetDirectoryName(Path.GetFullPath(builder.DataSource));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    }
}
