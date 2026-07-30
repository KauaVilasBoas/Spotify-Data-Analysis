using Microsoft.Extensions.Logging;
using SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Albums;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Artists;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;
using SpotifyDataAnalysis.SharedKernel.Messaging;
using SpotifyDataAnalysis.SharedKernel.Time;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Handler de <see cref="EnrichCatalogReferencesCommand"/>. Para artistas e álbuns, o fluxo é o mesmo:
///
/// <list type="number">
///   <item>pega uma fatia dos pendentes (<c>IsEnriched == false</c>) no repositório;</item>
///   <item>busca o perfil completo em <b>lote</b> na API — uma requisição por chunk, não por agregado;</item>
///   <item>indexa o retorno por id e aplica <see cref="Artist.EnrichProfile"/> / <see cref="Album.EnrichDetails"/>
///         em cada pendente que a API respondeu.</item>
/// </list>
///
/// A persistência + Outbox acontecem no commit do command (UnitOfWork/TransactionBehavior). Uma execução
/// cobre no máximo um <c>BatchSize</c> de cada tipo; o restante fica para a próxima passada (o job reexecuta),
/// o que mantém a transação curta e o consumo de rate limit previsível.
///
/// <b>Falha parcial não derruba o lote:</b> uma exceção ao enriquecer um agregado específico é registrada e
/// contada como <c>Failed</c>, e os demais seguem — o dado já avançado permanece. Ids que a API não retornou
/// (desconhecidos, removidos) são contados à parte como <c>NotFound</c>: não são erro, seguem pendentes e serão
/// retentados.
///
/// <para><b>Anti-starvation (dead-letter do enriquecimento):</b> todo desfecho que não enriqueceu — id ausente
/// do retorno OU exceção ao aplicá-lo — registra uma tentativa no agregado
/// (<see cref="Artist.RecordEnrichmentMiss"/> / <see cref="Album.RecordEnrichmentMiss"/>). Ao esgotar
/// <see cref="Artist.MaxEnrichmentAttempts"/> tentativas, o agregado sai da fila de pendentes
/// (<c>ListPendingEnrichment</c> o exclui), de modo que ids permanentemente irresolúveis não fiquem para
/// sempre na cabeça da fila re-queimando quota e bloqueando os pendentes reais atrás deles. Mesma ideia do
/// <c>MaxAttempts</c> do Outbox.</para>
/// </summary>
internal sealed class EnrichCatalogReferencesCommandHandler
    : ICommandHandler<EnrichCatalogReferencesCommand, EnrichCatalogReferencesResult>
{
    private readonly IArtistRepository _artists;
    private readonly IAlbumRepository _albums;
    private readonly ISpotifyClient _spotify;
    private readonly IClock _clock;
    private readonly ILogger<EnrichCatalogReferencesCommandHandler> _logger;

    public EnrichCatalogReferencesCommandHandler(
        IArtistRepository artists,
        IAlbumRepository albums,
        ISpotifyClient spotify,
        IClock clock,
        ILogger<EnrichCatalogReferencesCommandHandler> logger)
    {
        _artists = artists;
        _albums = albums;
        _spotify = spotify;
        _clock = clock;
        _logger = logger;
    }

    public async Task<EnrichCatalogReferencesResult> HandleAsync(
        EnrichCatalogReferencesCommand request, CancellationToken cancellationToken = default)
    {
        (int artistsEnriched, int artistsNotFound, int artistsFailed) =
            await EnrichArtistsAsync(request.BatchSize, cancellationToken);

        (int albumsEnriched, int albumsNotFound, int albumsFailed) =
            await EnrichAlbumsAsync(request.BatchSize, cancellationToken);

        return new EnrichCatalogReferencesResult(
            artistsEnriched, artistsNotFound, artistsFailed,
            albumsEnriched, albumsNotFound, albumsFailed);
    }

    private async Task<(int Enriched, int NotFound, int Failed)> EnrichArtistsAsync(
        int batchSize, CancellationToken cancellationToken)
    {
        IReadOnlyList<Artist> pending = await _artists.ListPendingEnrichmentAsync(batchSize, cancellationToken);
        if (pending.Count == 0)
            return (0, 0, 0);

        string[] ids = pending.Select(artist => artist.Id.Value).ToArray();
        IReadOnlyList<SpotifyArtist> profiles = await _spotify.GetArtistsAsync(ids, cancellationToken);

        Dictionary<string, SpotifyArtist> byId = profiles
            .GroupBy(profile => profile.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        int enriched = 0, notFound = 0, failed = 0;

        foreach (Artist artist in pending)
        {
            // Ausente do retorno: a API não conhece este id (removido, restrito). Não é erro — conta como
            // NotFound, registra a tentativa (para não travar a fila) e segue pendente.
            if (!byId.TryGetValue(artist.Id.Value, out SpotifyArtist? profile))
            {
                notFound++;
                artist.RecordEnrichmentMiss(_clock.UtcNow);
                continue;
            }

            try
            {
                artist.EnrichProfile(
                    profile.Name, Popularity.Of(profile.Popularity), profile.Followers, profile.Genres);
                enriched++;
            }
            catch (Exception exception)
            {
                // Um perfil malformado (ex.: popularidade fora de 0–100) não pode abortar o lote inteiro. É
                // falha real (Failed) e também conta como tentativa: um dado cronicamente inválido não deve
                // ficar preso na fila para sempre.
                failed++;
                artist.RecordEnrichmentMiss(_clock.UtcNow);
                _logger.LogWarning(exception,
                    "Falha ao enriquecer o artista {ArtistId}; segue pendente e será retentado.", artist.Id.Value);
            }
        }

        return (enriched, notFound, failed);
    }

    private async Task<(int Enriched, int NotFound, int Failed)> EnrichAlbumsAsync(
        int batchSize, CancellationToken cancellationToken)
    {
        IReadOnlyList<Album> pending = await _albums.ListPendingEnrichmentAsync(batchSize, cancellationToken);
        if (pending.Count == 0)
            return (0, 0, 0);

        string[] ids = pending.Select(album => album.Id.Value).ToArray();
        IReadOnlyList<SpotifyAlbum> details = await _spotify.GetAlbumsAsync(ids, cancellationToken);

        Dictionary<string, SpotifyAlbum> byId = details
            .GroupBy(detail => detail.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        int enriched = 0, notFound = 0, failed = 0;

        foreach (Album album in pending)
        {
            if (!byId.TryGetValue(album.Id.Value, out SpotifyAlbum? detail))
            {
                notFound++;
                album.RecordEnrichmentMiss(_clock.UtcNow);
                continue;
            }

            try
            {
                album.EnrichDetails(detail.Name, detail.ReleaseDate, detail.TotalTracks);
                enriched++;
            }
            catch (Exception exception)
            {
                failed++;
                album.RecordEnrichmentMiss(_clock.UtcNow);
                _logger.LogWarning(exception,
                    "Falha ao enriquecer o álbum {AlbumId}; segue pendente e será retentado.", album.Id.Value);
            }
        }

        return (enriched, notFound, failed);
    }
}
