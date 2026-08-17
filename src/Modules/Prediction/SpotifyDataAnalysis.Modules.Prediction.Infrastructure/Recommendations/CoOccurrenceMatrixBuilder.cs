using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using SpotifyDataAnalysis.Infrastructure.Data;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Recommendations;

/// <summary>Censo de uma reconstrução da matriz de co-ocorrência (E4.6): pares gravados e o custo.</summary>
/// <param name="PairsWritten">Quantos pares (arestas) foram materializados na tabela.</param>
/// <param name="ElapsedMilliseconds">Duração total do passo batch.</param>
public sealed record CoOccurrenceBuildResult(long PairsWritten, long ElapsedMilliseconds);

/// <summary>
/// O passo batch que materializa a matriz de co-ocorrência item-item (E4.6, DP-3): lê <c>catalog.playlists</c>
/// (jsonb <c>track_ids</c>, populado pelo Pichl no E4.5), forma os pares de faixas que co-ocorrem em ≥
/// <see cref="MinimumCoPlaylists"/> playlists, calcula o Jaccard e grava em <c>prediction.track_cooccurrence</c>.
///
/// <para><b>Por que um passo batch, e não on-the-fly (DP-3):</b> o self-join sobre <c>jsonb_array_elements_text</c>
/// em dezenas de milhares de playlists é caro demais para rodar a cada request. Pré-computar uma vez e servir da
/// tabela é o que torna a recomendação blend viável no volume do Pichl — a própria medição do E4.5 mostrou que a
/// agregação de pares leva segundos. Rodado pela CLI dev (<c>build-cooccurrence</c>), como os seeders.</para>
///
/// <para><b>Fronteira de módulo:</b> lê o schema <c>catalog</c> por SQL puro (Dapper via
/// <see cref="BaseDataAccess"/>), sem referenciar nenhum tipo .NET do Catalog — a mesma posição do E3.1/E4.1,
/// guardada pelos ArchTests. O cross-schema é só no SQL, dentro do mesmo banco.</para>
///
/// <para><b>Corte por <see cref="MinimumCoPlaylists"/> ≥ 2:</b> um par que co-ocorre numa ÚNICA playlist é ruído
/// (pode ser coincidência de uma lista aleatória); exigir repetição em ≥ 2 playlists é o gate de sinal do E4.5 e
/// mantém a tabela num tamanho tratável (~1M pares medidos, contra ~3,3M sem o corte).</para>
/// </summary>
public sealed class CoOccurrenceMatrixBuilder : BaseDataAccess
{
    /// <summary>Mínimo de playlists compartilhadas para um par entrar na matriz — o gate de sinal (≥ 2 = repetível).</summary>
    public const int MinimumCoPlaylists = 2;

    /// <summary>
    /// Reconstrói a matriz do zero: trunca a tabela e a repopula a partir das playlists atuais. Idempotente por
    /// construção — rodar de novo sobre o mesmo estado das playlists leva à mesma tabela.
    ///
    /// <para>Tudo num único statement no servidor (<c>INSERT ... SELECT</c>): o Postgres forma os pares, conta e
    /// calcula o Jaccard sem trazer nada para o processo — o insight que mantém o custo de memória do host baixo
    /// mesmo com milhões de pares intermediários.</para>
    /// </summary>
    internal static readonly string RebuildSql =
        $"""
        TRUNCATE TABLE prediction.track_cooccurrence;

        INSERT INTO prediction.track_cooccurrence
            (track_id_low, track_id_high, co_playlists, playlists_low, playlists_high, jaccard)
        WITH exploded AS (
            SELECT p.id AS playlist_id, elem AS track_id
            FROM catalog.playlists AS p,
                 LATERAL jsonb_array_elements_text(p.track_ids) AS elem
        ),
        track_totals AS (
            SELECT track_id, count(DISTINCT playlist_id) AS playlists
            FROM exploded
            GROUP BY track_id
        ),
        pairs AS (
            SELECT a.track_id AS track_id_low,
                   b.track_id AS track_id_high,
                   count(DISTINCT a.playlist_id) AS co_playlists
            FROM exploded AS a
            JOIN exploded AS b
              ON a.playlist_id = b.playlist_id
             AND a.track_id < b.track_id
            GROUP BY a.track_id, b.track_id
            HAVING count(DISTINCT a.playlist_id) >= {MinimumCoPlaylists}
        )
        SELECT pr.track_id_low,
               pr.track_id_high,
               pr.co_playlists,
               tl.playlists AS playlists_low,
               th.playlists AS playlists_high,
               pr.co_playlists::double precision
                   / (tl.playlists + th.playlists - pr.co_playlists) AS jaccard
        FROM pairs AS pr
        JOIN track_totals AS tl ON tl.track_id = pr.track_id_low
        JOIN track_totals AS th ON th.track_id = pr.track_id_high;
        """;

    private const string CountSql = "SELECT count(*) FROM prediction.track_cooccurrence;";

    private readonly ILogger<CoOccurrenceMatrixBuilder> _logger;

    public CoOccurrenceMatrixBuilder(
        DbConnectionFactory connectionFactory, ILogger<CoOccurrenceMatrixBuilder> logger)
        : base(connectionFactory)
    {
        _logger = logger;
    }

    public async Task<CoOccurrenceBuildResult> RebuildAsync(CancellationToken cancellationToken = default)
    {
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using IDbConnection connection = await OpenConnectionAsync();

        // O passo agregado pode levar minutos no volume do Pichl; sem timeout explícito o Npgsql cortaria em 30 s.
        await connection.ExecuteAsync(new CommandDefinition(
            RebuildSql, commandTimeout: 1_800, cancellationToken: cancellationToken));

        long pairs = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            CountSql, cancellationToken: cancellationToken));

        stopwatch.Stop();

        var result = new CoOccurrenceBuildResult(pairs, (long)stopwatch.Elapsed.TotalMilliseconds);

        _logger.LogInformation(
            "Matriz de co-ocorrência reconstruída: {Pairs} pares em {ElapsedMs} ms.",
            result.PairsWritten, result.ElapsedMilliseconds);

        return result;
    }
}
