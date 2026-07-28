using SpotifyDataAnalysis.SharedKernel.Domain;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks.Events;

/// <summary>
/// Emitido quando uma <see cref="Track"/> é registrada no catálogo (ingestão). Interno ao bounded context;
/// será traduzido para um integration event no Outbox (E1.3) para que outros módulos (Analytics/Prediction)
/// reajam à chegada de novas faixas.
/// </summary>
public sealed record TrackRegisteredDomainEvent(string TrackId, DateTime OccurredOnUtc) : IDomainEvent;
