using SpotifyDataAnalysis.SharedKernel.Domain;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks.Events;

/// <summary>
/// Emitido quando uma <see cref="Track"/> é registrada no catálogo (ingestão). Interno ao bounded context;
/// traduzido para o integration event <c>TrackIngested</c> no Outbox (E1.3) para que outros módulos
/// (Analytics/Prediction) reajam à chegada de novas faixas. Carrega o mínimo que o consumidor precisa
/// (id, nome, popularidade) sem forçar um round-trip ao catálogo.
/// </summary>
public sealed record TrackRegisteredDomainEvent(
    string TrackId, string Name, int Popularity, DateTime OccurredOnUtc) : IDomainEvent;
