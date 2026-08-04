using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// E3.6 — o ranking de importância de features passa a ser gravado junto da versão do modelo.
    ///
    /// <para>O default <c>'[]'</c> existe só para preencher as versões já registradas antes desta fatia, e é
    /// removido logo em seguida: mantê-lo faria o banco divergir do modelo, e a próxima migration tentaria
    /// derrubá-lo sozinha. Lista vazia nessas linhas significa "não foi medido", que é diferente de "nenhuma
    /// feature importa".</para>
    /// </summary>
    public partial class AddModelFeatureImportance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "feature_importance",
                schema: "prediction",
                table: "model_versions",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.Sql(
                "ALTER TABLE prediction.model_versions ALTER COLUMN feature_importance DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "feature_importance",
                schema: "prediction",
                table: "model_versions");
        }
    }
}
