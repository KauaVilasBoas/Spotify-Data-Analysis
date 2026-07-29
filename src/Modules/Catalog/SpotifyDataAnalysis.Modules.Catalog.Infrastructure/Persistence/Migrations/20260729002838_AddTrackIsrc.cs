using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackIsrc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Coluna NULLABLE sem default: "sem ISRC" e um estado legitimo da faixa (parte do catalogo do
            // Spotify nao expoe external_ids.isrc), entao NULL significa exatamente isso. As faixas ja
            // ingeridas ganham o codigo no proximo ciclo de coleta, que so preenche o ISRC e nunca o apaga.
            //
            // Indice NAO-UNICO de proposito: o mesmo ISRC aparece em faixas distintas do Spotify
            // (relancamentos, edicoes por mercado). Ele existe para servir o match por ISRC (E1.9).
            migrationBuilder.AddColumn<string>(
                name: "isrc",
                schema: "catalog",
                table: "tracks",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tracks_isrc",
                schema: "catalog",
                table: "tracks",
                column: "isrc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tracks_isrc",
                schema: "catalog",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "isrc",
                schema: "catalog",
                table: "tracks");
        }
    }
}
