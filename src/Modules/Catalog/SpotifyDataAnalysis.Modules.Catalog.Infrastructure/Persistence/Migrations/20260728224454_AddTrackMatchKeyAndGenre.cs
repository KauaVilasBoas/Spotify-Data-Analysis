using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackMatchKeyAndGenre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // O default e "|" (a forma canonica da chave VAZIA), nao "": assim uma linha pre-existente e
            // reconhecida como "sem chave" ao ser reidratada e nunca casa por engano, enquanto "" seria um
            // valor fora do formato. Faixas anteriores a esta migration ganham a chave real no proximo ciclo
            // de coleta, que recalcula MatchKey ao reaplicar o retrato da API.
            //
            // O genero das audio-features nao aparece aqui de proposito: elas sao persistidas como JSON numa
            // unica coluna (audio_features), entao o campo novo nao altera o schema.
            migrationBuilder.AddColumn<string>(
                name: "match_key",
                schema: "catalog",
                table: "tracks",
                type: "character varying(800)",
                maxLength: 800,
                nullable: false,
                defaultValue: "|");

            migrationBuilder.CreateIndex(
                name: "ix_tracks_match_key",
                schema: "catalog",
                table: "tracks",
                column: "match_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tracks_match_key",
                schema: "catalog",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "match_key",
                schema: "catalog",
                table: "tracks");
        }
    }
}
