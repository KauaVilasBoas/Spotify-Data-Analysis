using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArtistAlbumPlaylist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // tracks.artist_ids guardava um array de ids (["a1","a2"]); tracks.artists guarda os créditos
            // completos ([{"Id":"a1","Name":"Queen"}]). Não é um rename: o CONTEÚDO é incompatível e não há
            // nome de artista no dado antigo para reconstruí-lo. Em vez de inventar nomes (que contaminariam
            // a chave de casamento com o dataset Kaggle), a coluna é recriada vazia — o próximo ciclo de
            // coleta, que é idempotente, repovoa os créditos via Track.RefreshFromSource.
            migrationBuilder.DropColumn(
                name: "artist_ids",
                schema: "catalog",
                table: "tracks");

            migrationBuilder.AddColumn<string>(
                name: "artists",
                schema: "catalog",
                table: "tracks",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.CreateTable(
                name: "albums",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    release_date_raw = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    release_year = table.Column<int>(type: "integer", nullable: true),
                    release_precision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    release_date = table.Column<DateOnly>(type: "date", nullable: true),
                    total_tracks = table.Column<int>(type: "integer", nullable: false),
                    is_enriched = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_albums", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "artists",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    popularity = table.Column<int>(type: "integer", nullable: false),
                    followers = table.Column<int>(type: "integer", nullable: false),
                    is_enriched = table.Column<bool>(type: "boolean", nullable: false),
                    genres = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artists", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "playlists",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    owner_display_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    last_ingested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    track_ids = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playlists", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_albums_release_year",
                schema: "catalog",
                table: "albums",
                column: "release_year");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "albums",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "artists",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "playlists",
                schema: "catalog");

            migrationBuilder.DropColumn(
                name: "artists",
                schema: "catalog",
                table: "tracks");

            migrationBuilder.AddColumn<string>(
                name: "artist_ids",
                schema: "catalog",
                table: "tracks",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");
        }
    }
}
