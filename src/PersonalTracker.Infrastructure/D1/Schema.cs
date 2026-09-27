namespace PersonalTracker.Infrastructure.D1;

/// <summary>DDL applied at startup. D1 has no EF migrations, so this is the schema's single source of truth.</summary>
internal static class Schema
{
    private const string Sql = """
                               PRAGMA foreign_keys = ON;

                               CREATE TABLE IF NOT EXISTS Users (
                                 Id TEXT PRIMARY KEY,
                                 Email TEXT NOT NULL,
                                 NormalizedEmail TEXT NOT NULL,
                                 DisplayName TEXT NOT NULL,
                                 PasswordHash TEXT NOT NULL,
                                 CreatedAt TEXT NOT NULL
                               );
                               CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_NormalizedEmail ON Users (NormalizedEmail);

                               CREATE TABLE IF NOT EXISTS RefreshTokens (
                                 Id TEXT PRIMARY KEY,
                                 UserId TEXT NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
                                 TokenHash TEXT NOT NULL,
                                 CreatedAt TEXT NOT NULL,
                                 ExpiresAt TEXT NOT NULL,
                                 RevokedAt TEXT NULL,
                                 ReplacedByTokenHash TEXT NULL,
                                 CreatedByIp TEXT NULL
                               );
                               CREATE UNIQUE INDEX IF NOT EXISTS IX_RefreshTokens_TokenHash ON RefreshTokens (TokenHash);
                               CREATE INDEX IF NOT EXISTS IX_RefreshTokens_UserId ON RefreshTokens (UserId);

                               CREATE TABLE IF NOT EXISTS Collections (
                                 Id TEXT PRIMARY KEY,
                                 UserId TEXT NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
                                 Name TEXT NOT NULL,
                                 Description TEXT NULL,
                                 Icon TEXT NOT NULL,
                                 CoverType TEXT NOT NULL,
                                 CoverValue TEXT NULL,
                                 CoverUpdatedAt TEXT NULL,
                                 CreatedAt TEXT NOT NULL,
                                 UpdatedAt TEXT NOT NULL
                               );
                               CREATE INDEX IF NOT EXISTS IX_Collections_UserId ON Collections (UserId);

                               CREATE TABLE IF NOT EXISTS Fields (
                                 Id TEXT PRIMARY KEY,
                                 CollectionId TEXT NOT NULL REFERENCES Collections(Id) ON DELETE CASCADE,
                                 Name TEXT NOT NULL,
                                 Key TEXT NOT NULL,
                                 Description TEXT NULL,
                                 Type TEXT NOT NULL,
                                 Required INTEGER NOT NULL,
                                 SortOrder INTEGER NOT NULL,
                                 Config TEXT NOT NULL,
                                 Aggregation TEXT NOT NULL,
                                 IsTitle INTEGER NOT NULL,
                                 ShowInList INTEGER NOT NULL,
                                 CreatedAt TEXT NOT NULL
                               );
                               CREATE UNIQUE INDEX IF NOT EXISTS IX_Fields_CollectionId_Key ON Fields (CollectionId, Key);

                               CREATE TABLE IF NOT EXISTS Records (
                                 Id TEXT PRIMARY KEY,
                                 CollectionId TEXT NOT NULL REFERENCES Collections(Id) ON DELETE CASCADE,
                                 UserId TEXT NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
                                 DataJson TEXT NOT NULL,
                                 CreatedAt TEXT NOT NULL,
                                 UpdatedAt TEXT NOT NULL,
                                 Version INTEGER NOT NULL
                               );
                               CREATE INDEX IF NOT EXISTS IX_Records_CollectionId ON Records (CollectionId);
                               CREATE INDEX IF NOT EXISTS IX_Records_UserId ON Records (UserId);
                               """;

    /// <summary>Split into individual statements: sent one per request, since a single D1 REST call is one statement.</summary>
    public static readonly string[] Statements = Sql
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(s => s.Length > 0)
        .ToArray();
}