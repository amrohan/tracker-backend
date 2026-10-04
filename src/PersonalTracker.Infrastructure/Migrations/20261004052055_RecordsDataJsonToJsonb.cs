using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecordsDataJsonToJsonb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename DataJson → Data in-place (no data loss).
            migrationBuilder.Sql("""
                ALTER TABLE "Records" RENAME COLUMN "DataJson" TO "Data";
                """);

            // Cast TEXT → jsonb using stored value; existing JSON strings are valid jsonb.
            migrationBuilder.Sql("""
                ALTER TABLE "Records"
                    ALTER COLUMN "Data" TYPE jsonb
                    USING "Data"::jsonb;
                """);

            // Drop old single-column indexes that were created by the initial migration.
            migrationBuilder.DropIndex(
                name: "IX_Records_UserId",
                table: "Records");

            // Note: IX_Records_CollectionId was not in the initial migration (only UserId was),
            // so we only drop the one that exists.

            // Composite B-tree index — covers the dominant WHERE UserId = ? AND CollectionId = ? pattern.
            migrationBuilder.CreateIndex(
                name: "IX_Records_UserId_CollectionId",
                table: "Records",
                columns: new[] { "UserId", "CollectionId" });

            // Timestamp indexes for sorting / range queries.
            migrationBuilder.CreateIndex(
                name: "IX_Records_CreatedAt",
                table: "Records",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Records_UpdatedAt",
                table: "Records",
                column: "UpdatedAt");

            // GIN index enables fast @>, ?, ?| containment/key-existence queries on the jsonb column.
            migrationBuilder.Sql("""
                CREATE INDEX "IX_Records_Data_gin"
                    ON "Records" USING gin ("Data" jsonb_path_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_Records_Data_gin";
                """);

            migrationBuilder.DropIndex(
                name: "IX_Records_UpdatedAt",
                table: "Records");

            migrationBuilder.DropIndex(
                name: "IX_Records_CreatedAt",
                table: "Records");

            migrationBuilder.DropIndex(
                name: "IX_Records_UserId_CollectionId",
                table: "Records");

            // Restore the old single-column UserId index.
            migrationBuilder.CreateIndex(
                name: "IX_Records_UserId",
                table: "Records",
                column: "UserId");

            // Revert jsonb → TEXT and rename back.
            migrationBuilder.Sql("""
                ALTER TABLE "Records"
                    ALTER COLUMN "Data" TYPE TEXT
                    USING "Data"::text;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "Records" RENAME COLUMN "Data" TO "DataJson";
                """);
        }
    }
}
