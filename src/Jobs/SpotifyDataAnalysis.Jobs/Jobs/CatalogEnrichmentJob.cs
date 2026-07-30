using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotifyDataAnalysis.Jobs.Configuration;
using SpotifyDataAnalysis.Jobs.Scheduling;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Jobs.Jobs;

/// <summary>
/// Enriquecimento agendado das referências do catálogo (E1.8): a cada tick, despacha um
/// <see cref="EnrichCatalogReferencesCommand"/> que carrega da API o perfil completo de uma fatia dos
/// artistas/álbuns ainda pendentes (<c>IsEnriched == false</c>).
///
/// <b>Trabalho de fundo, não request:</b> o enriquecimento pode percorrer milhares de agregados e é o maior
/// consumidor de rate limit do projeto — por isso é um job, não um endpoint síncrono (DP-1 do card). A
/// idempotência é propriedade do command: os já enriquecidos deixam de ser candidatos, então o job pode rodar
/// quantas vezes quiser e vai apenas drenando o backlog um lote por vez, sem cursor nem estado próprio.
///
/// Como cada tick tem sua própria transação (a base abre um escopo por tick), o que já foi enriquecido
/// permanece mesmo que um tick posterior falhe — a base registra o erro e retenta no próximo intervalo.
///
/// O job conhece o módulo Catalog apenas pelo seu ponto de entrada de aplicação (o command), nunca pelo
/// Domain — a mesma fronteira que o Host respeita.
/// </summary>
public sealed class CatalogEnrichmentJob : TimedBackgroundService
{
    private readonly CatalogEnrichmentOptions _options;

    public CatalogEnrichmentJob(
        IServiceScopeFactory scopeFactory,
        IOptions<JobsOptions> options,
        ILogger<CatalogEnrichmentJob> logger)
        : base(scopeFactory, logger)
    {
        _options = options.Value.CatalogEnrichment;
    }

    /// <inheritdoc />
    protected override TimeSpan Interval => _options.Interval;

    /// <inheritdoc />
    protected override async Task ExecuteTickAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        IMediator mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // BatchSize 0 (não configurado) cede ao default do próprio command.
        EnrichCatalogReferencesCommand command = _options.BatchSize > 0
            ? new EnrichCatalogReferencesCommand(_options.BatchSize)
            : new EnrichCatalogReferencesCommand();

        EnrichCatalogReferencesResult result = await mediator.SendAsync(command, cancellationToken);

        Logger.LogInformation(
            "Enriquecimento do catálogo: {TotalEnriched} enriquecidos ({ArtistsEnriched} artistas, " +
            "{AlbumsEnriched} álbuns); {TotalNotFound} não retornados pela API ({ArtistsNotFound} artistas, " +
            "{AlbumsNotFound} álbuns); {TotalFailed} falhas reais ({ArtistsFailed} artistas, {AlbumsFailed} álbuns).",
            result.TotalEnriched, result.ArtistsEnriched, result.AlbumsEnriched,
            result.TotalNotFound, result.ArtistsNotFound, result.AlbumsNotFound,
            result.TotalFailed, result.ArtistsFailed, result.AlbumsFailed);
    }
}
