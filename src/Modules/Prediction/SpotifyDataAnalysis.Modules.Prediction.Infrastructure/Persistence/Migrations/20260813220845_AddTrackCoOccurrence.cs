using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// E4.6 — a tabela de pares de co-ocorrência materializada (DP-3). É uma tabela de LEITURA pura, não um agregado
    /// EF: guarda, para cada par de faixas do catálogo que co-ocorre em playlists (Pichl, E4.5), quantas playlists
    /// as duas compartilham e o Jaccard já calculado. Populada por um passo batch (CLI <c>build-cooccurrence</c>) e
    /// lida por Dapper — nunca pelo self-join sobre jsonb a cada request, que no volume do Pichl é inviável.
    ///
    /// <para><b>Par canônico ordenado (<c>track_id_low &lt; track_id_high</c>):</b> cada par é gravado UMA vez, na
    /// ordem lexicográfica dos ids. A consulta por semente busca os dois lados (a semente como low OU como high), o
    /// que evita duplicar cada aresta e corta a tabela pela metade.</para>
    ///
    /// <para>Criada por SQL direto (não pelo model builder): nenhuma entidade a mapeia — forçá-la a virar agregado
    /// só para existir uma tabela analítica seria cerimônia. Por isso o snapshot do EF não a conhece, e é esta
    /// migration que a cria e derruba.</para>
    /// </summary>
    public partial class AddTrackCoOccurrence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE prediction.track_cooccurrence (
                    track_id_low   varchar(64)      NOT NULL,
                    track_id_high  varchar(64)      NOT NULL,
                    co_playlists   integer          NOT NULL,
                    playlists_low  integer          NOT NULL,
                    playlists_high integer          NOT NULL,
                    jaccard        double precision NOT NULL,
                    CONSTRAINT pk_track_cooccurrence PRIMARY KEY (track_id_low, track_id_high)
                );
                """);

            // Índice pelo lado "high" para a consulta por semente achar as arestas onde a semente é o id maior sem
            // varrer a tabela (o lado "low" já é coberto pela PK).
            migrationBuilder.Sql(
                "CREATE INDEX ix_track_cooccurrence_high ON prediction.track_cooccurrence (track_id_high);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS prediction.track_cooccurrence;");
        }
    }
}
