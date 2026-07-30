using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "audio_features",
                schema: "catalog",
                table: "tracks",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "audio_features",
                schema: "catalog",
                table: "tracks");
        }
    }
}
