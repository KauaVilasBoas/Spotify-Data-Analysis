using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Ingesta as faixas de uma playlist do Spotify no catálogo: coleta via <c>ISpotifyClient</c>, registra as
/// novas como <c>Track</c> e atualiza a popularidade das já existentes. É um command (muda estado) —
/// o SaveChanges + Outbox são disparados pelo TransactionBehavior/UnitOfWork do módulo.
/// </summary>
public sealed record IngestPlaylistCommand(string SpotifyPlaylistId) : ICommand<IngestPlaylistResult>;

/// <summary>Resumo da ingestão: quantas faixas foram registradas, atualizadas, puladas e o total processado.</summary>
public sealed record IngestPlaylistResult(int Ingested, int Updated, int Skipped, int Total);
