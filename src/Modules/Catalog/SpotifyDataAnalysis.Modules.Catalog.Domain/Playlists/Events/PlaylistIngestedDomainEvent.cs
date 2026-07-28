using SpotifyDataAnalysis.SharedKernel.Domain;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists.Events;

/// <summary>
/// Emitido quando uma playlist-semente termina um ciclo de coleta. Interno ao bounded context; traduzido
/// para o integration event <c>PlaylistIngested</c> no Outbox, que sinaliza a outros módulos que um lote de
/// faixas acabou de entrar no catálogo (Analytics reprojeta, Prediction reavalia elegibilidade de treino).
/// </summary>
public sealed record PlaylistIngestedDomainEvent(
    string PlaylistId, string Name, int TrackCount, DateTime OccurredOnUtc) : IDomainEvent;
