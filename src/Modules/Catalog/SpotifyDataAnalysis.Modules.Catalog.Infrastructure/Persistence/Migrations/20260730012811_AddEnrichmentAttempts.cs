using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrichmentAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "enrichment_attempts",
                schema: "catalog",
                table: "artists",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_enrichment_attempt_utc",
                schema: "catalog",
                table: "artists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "enrichment_attempts",
                schema: "catalog",
                table: "albums",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_enrichment_attempt_utc",
                schema: "catalog",
                table: "albums",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_artists_enrichment_pending",
                schema: "catalog",
                table: "artists",
                columns: new[] { "is_enriched", "enrichment_attempts" });

            migrationBuilder.CreateIndex(
                name: "ix_albums_enrichment_pending",
                schema: "catalog",
                table: "albums",
                columns: new[] { "is_enriched", "enrichment_attempts" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_artists_enrichment_pending",
                schema: "catalog",
                table: "artists");

            migrationBuilder.DropIndex(
                name: "ix_albums_enrichment_pending",
                schema: "catalog",
                table: "albums");

            migrationBuilder.DropColumn(
                name: "enrichment_attempts",
                schema: "catalog",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "last_enrichment_attempt_utc",
                schema: "catalog",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "enrichment_attempts",
                schema: "catalog",
                table: "albums");

            migrationBuilder.DropColumn(
                name: "last_enrichment_attempt_utc",
                schema: "catalog",
                table: "albums");
        }
    }
}
