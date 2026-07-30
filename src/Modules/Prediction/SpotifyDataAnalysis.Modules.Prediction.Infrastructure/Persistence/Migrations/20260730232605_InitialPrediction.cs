using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPrediction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "prediction");

            migrationBuilder.CreateTable(
                name: "model_versions",
                schema: "prediction",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    trained_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    trainer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    seed = table.Column<int>(type: "integer", nullable: false),
                    test_fraction = table.Column<double>(type: "double precision", nullable: false),
                    training_sample_count = table.Column<long>(type: "bigint", nullable: false),
                    test_sample_count = table.Column<long>(type: "bigint", nullable: false),
                    trained_on_imputed = table.Column<bool>(type: "boolean", nullable: false),
                    model_r_squared = table.Column<double>(type: "double precision", nullable: false),
                    model_mae = table.Column<double>(type: "double precision", nullable: false),
                    model_rmse = table.Column<double>(type: "double precision", nullable: false),
                    baseline_r_squared = table.Column<double>(type: "double precision", nullable: false),
                    baseline_mae = table.Column<double>(type: "double precision", nullable: false),
                    baseline_rmse = table.Column<double>(type: "double precision", nullable: false),
                    artifact = table.Column<byte[]>(type: "bytea", nullable: false),
                    artifact_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    features = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_model_versions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_model_versions_single_current",
                schema: "prediction",
                table: "model_versions",
                column: "status",
                unique: true,
                filter: "status = 'Current'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_versions",
                schema: "prediction");
        }
    }
}
