using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PersonalTracker.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(254).IsRequired();
        b.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        b.HasIndex(x => x.NormalizedEmail).IsUnique();
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> b)
    {
        b.ToTable("Collections");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Icon).HasMaxLength(32).IsRequired();
        b.Property(x => x.CoverType).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.CoverValue).HasMaxLength(200);
        b.HasIndex(x => x.UserId);

        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Fields).WithOne(f => f.Collection).HasForeignKey(f => f.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class FieldConfiguration : IEntityTypeConfiguration<Field>
{
    public void Configure(EntityTypeBuilder<Field> b)
    {
        b.ToTable("Fields");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.Key).HasMaxLength(80).IsRequired();
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(24).IsRequired();
        b.Property(x => x.Aggregation).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.HasIndex(x => new { x.CollectionId, x.Key }).IsUnique();

        var converter = new ValueConverter<FieldConfig, string>(
            v => FieldConfigJson.Serialize(v),
            s => FieldConfigJson.Deserialize(s));
        var comparer = new ValueComparer<FieldConfig>(
            (a, c) => FieldConfigJson.Serialize(a) == FieldConfigJson.Serialize(c),
            v => FieldConfigJson.Serialize(v).GetHashCode(),
            v => FieldConfigJson.Deserialize(FieldConfigJson.Serialize(v)));
        b.Property(x => x.Config).HasConversion(converter, comparer).HasColumnType("TEXT").IsRequired();
    }
}

public sealed class TrackerRecordConfiguration : IEntityTypeConfiguration<TrackerRecord>
{
    public void Configure(EntityTypeBuilder<TrackerRecord> b)
    {
        b.ToTable("Records");
        b.HasKey(x => x.Id);
        b.Property(x => x.Data).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.UserId, x.CollectionId });
        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => x.UpdatedAt);

        b.HasOne<Collection>()
            .WithMany()
            .HasForeignKey(x => x.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Static helpers so the value converter/comparer expression trees stay simple.</summary>
public static class FieldConfigJson
{
    public static string Serialize(FieldConfig? config) =>
        JsonSerializer.Serialize(config ?? new FieldConfig(), JsonDefaults.Options);

    public static FieldConfig Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new FieldConfig()
            : JsonSerializer.Deserialize<FieldConfig>(json, JsonDefaults.Options) ?? new FieldConfig();
}