using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Ingesta uma playlist-semente do Spotify no catálogo: registra/atualiza a própria playlist, as faixas, e
/// os artistas e álbuns que elas referenciam. É um command (muda estado) — o SaveChanges + Outbox são
/// disparados pelo TransactionBehavior/UnitOfWork do módulo.
///
/// <b>Idempotente</b>: rodar duas vezes sobre a mesma playlist não duplica nada — faixas já catalogadas são
/// atualizadas (a popularidade varia com o tempo) e a playlist passa a refletir o último retrato visto.
/// </summary>
public sealed record IngestPlaylistCommand(string SpotifyPlaylistId) : ICommand<IngestPlaylistResult>;

/// <summary>
/// Resumo de um ciclo de coleta: faixas registradas pela primeira vez, atualizadas, puladas (sem id/nome —
/// arquivos locais ou itens removidos), repetidas dentro da própria playlist e o total lido da API.
/// </summary>
public sealed record IngestPlaylistResult(
    int Ingested,
    int Updated,
    int Skipped,
    int Duplicates,
    int Total)
{
    /// <summary>Faixas distintas efetivamente catalogadas neste ciclo.</summary>
    public int Cataloged => Ingested + Updated;
}
