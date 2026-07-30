using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;
using SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Albums;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Artists;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;
using SpotifyDataAnalysis.Modules.Catalog.Tests.Fakes;
using SpotifyDataAnalysis.SharedKernel.Time;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Application;

/// <summary>
/// Testes do <see cref="EnrichCatalogReferencesCommandHandler"/> (E1.8): carrega o perfil completo de artistas
/// e álbuns pendentes (<c>IsEnriched == false</c>) via os endpoints em lote da API, é idempotente e não deixa
/// uma falha isolada abortar o lote. Tudo com cliente Spotify e repositórios fakes — sem rede/banco.
/// </summary>
public sealed class EnrichCatalogReferencesTests
{
    /// <summary>
    /// Cliente Spotify de enriquecimento: responde os endpoints EM LOTE a partir de um dicionário de perfis e
    /// registra quantas chamadas de lote recebeu — o que permite provar que o handler não faz uma requisição
    /// por agregado. Os endpoints unitários e de playlist não são usados aqui e falham alto se forem chamados.
    /// </summary>
    private sealed class FakeEnrichmentClient : ISpotifyClient
    {
        private readonly Dictionary<string, SpotifyArtist> _artists;
        private readonly Dictionary<string, SpotifyAlbum> _albums;

        public FakeEnrichmentClient(
            IEnumerable<SpotifyArtist>? artists = null, IEnumerable<SpotifyAlbum>? albums = null)
        {
            _artists = (artists ?? []).ToDictionary(a => a.Id, StringComparer.Ordinal);
            _albums = (albums ?? []).ToDictionary(a => a.Id, StringComparer.Ordinal);
        }

        public int ArtistBatchCalls { get; private set; }
        public int AlbumBatchCalls { get; private set; }

        public Task<IReadOnlyList<SpotifyArtist>> GetArtistsAsync(
            IReadOnlyCollection<string> artistIds, CancellationToken cancellationToken = default)
        {
            ArtistBatchCalls++;
            IReadOnlyList<SpotifyArtist> found = artistIds
                .Where(_artists.ContainsKey)
                .Select(id => _artists[id])
                .ToList();
            return Task.FromResult(found);
        }

        public Task<IReadOnlyList<SpotifyAlbum>> GetAlbumsAsync(
            IReadOnlyCollection<string> albumIds, CancellationToken cancellationToken = default)
        {
            AlbumBatchCalls++;
            IReadOnlyList<SpotifyAlbum> found = albumIds
                .Where(_albums.ContainsKey)
                .Select(id => _albums[id])
                .ToList();
            return Task.FromResult(found);
        }

        // Endpoints fora do escopo do enriquecimento — não devem ser tocados por este caso de uso.
        public Task<SpotifyArtist?> GetArtistAsync(string artistId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SpotifyAlbum?> GetAlbumAsync(string albumId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SpotifyPlaylist?> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SpotifyPlaylistTracksPage> GetPlaylistTracksAsync(
            string playlistId, int offset, int limit, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<SpotifyTrack> StreamPlaylistTracksAsync(
            string playlistId, int pageSize = 100, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SpotifyTrack?> GetTrackAsync(string trackId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static readonly IClock Clock = new FixedClock(new DateTime(2026, 07, 29, 0, 0, 0, DateTimeKind.Utc));

    private static EnrichCatalogReferencesCommandHandler Build(
        InMemoryArtistRepository artists, InMemoryAlbumRepository albums, ISpotifyClient client)
        => new(artists, albums, client, Clock, NullLogger<EnrichCatalogReferencesCommandHandler>.Instance);

    private static void SeedArtist(InMemoryArtistRepository artists, string id, string name = "Queen")
        => artists.AddAsync(Artist.RegisterFromReference(SpotifyArtistId.Of(id), name)).GetAwaiter().GetResult();

    private static void SeedAlbum(InMemoryAlbumRepository albums, string id, string name = "A Night at the Opera")
        => albums.AddAsync(Album.RegisterFromReference(SpotifyAlbumId.Of(id), name)).GetAwaiter().GetResult();

    [Fact]
    public async Task Enriches_PendingArtist_WithTheProfileFromTheApi()
    {
        var artists = new InMemoryArtistRepository();
        var albums = new InMemoryAlbumRepository();
        SeedArtist(artists, "a1");

        var client = new FakeEnrichmentClient(
            artists: [new SpotifyArtist("a1", "Queen", Popularity: 84, Followers: 45_000_000, Genres: ["rock", "glam rock"])]);

        EnrichCatalogReferencesResult result =
            await Build(artists, albums, client).HandleAsync(new EnrichCatalogReferencesCommand());

        Assert.Equal(1, result.ArtistsEnriched);
        Assert.Equal(0, result.ArtistsNotFound);
        Assert.Equal(0, result.ArtistsFailed);

        Artist enriched = artists.Store["a1"];
        Assert.True(enriched.IsEnriched);
        Assert.NotEqual(Popularity.Unknown, enriched.Popularity); // popularidade real, não "desconhecida"
        Assert.Equal(84, enriched.Popularity.Value);
        Assert.Equal(45_000_000, enriched.Followers);
        Assert.Equal(new[] { "rock", "glam rock" }, enriched.Genres);
    }

    [Fact]
    public async Task Enriches_PendingAlbum_WithTheDetailsFromTheApi()
    {
        var artists = new InMemoryArtistRepository();
        var albums = new InMemoryAlbumRepository();
        SeedAlbum(albums, "al1");

        var client = new FakeEnrichmentClient(
            albums: [new SpotifyAlbum("al1", "A Night at the Opera", ReleaseDate: "1975-11-21", TotalTracks: 12)]);

        EnrichCatalogReferencesResult result =
            await Build(artists, albums, client).HandleAsync(new EnrichCatalogReferencesCommand());

        Assert.Equal(1, result.AlbumsEnriched);
        Assert.Equal(0, result.AlbumsNotFound);
        Assert.Equal(0, result.AlbumsFailed);

        Album enriched = albums.Store["al1"];
        Assert.True(enriched.IsEnriched);
        Assert.NotNull(enriched.ReleaseDate);
        Assert.Equal(12, enriched.TotalTracks);
    }

    [Fact]
    public async Task Uses_TheBatchEndpoints_OneCallPerType_NotOnePerAggregate()
    {
        var artists = new InMemoryArtistRepository();
        var albums = new InMemoryAlbumRepository();
        SeedArtist(artists, "a1");
        SeedArtist(artists, "a2");
        SeedArtist(artists, "a3");
        SeedAlbum(albums, "al1");
        SeedAlbum(albums, "al2");

        var client = new FakeEnrichmentClient(
            artists:
            [
                new SpotifyArtist("a1", "One", 10, 1, []),
                new SpotifyArtist("a2", "Two", 20, 2, []),
                new SpotifyArtist("a3", "Three", 30, 3, [])
            ],
            albums:
            [
                new SpotifyAlbum("al1", "Al One", "2001", 5),
                new SpotifyAlbum("al2", "Al Two", "2002", 6)
            ]);

        EnrichCatalogReferencesResult result =
            await Build(artists, albums, client).HandleAsync(new EnrichCatalogReferencesCommand());

        // Um lote basta (3 artistas < 50/chamada, 2 álbuns < 20/chamada): uma chamada de cada tipo, não 5.
        Assert.Equal(1, client.ArtistBatchCalls);
        Assert.Equal(1, client.AlbumBatchCalls);
        Assert.Equal(3, result.ArtistsEnriched);
        Assert.Equal(2, result.AlbumsEnriched);
    }

    [Fact]
    public async Task IsIdempotent_AlreadyEnrichedAggregatesAreNotCandidatesAgain()
    {
        var artists = new InMemoryArtistRepository();
        var albums = new InMemoryAlbumRepository();
        SeedArtist(artists, "a1");

        var client = new FakeEnrichmentClient(
            artists: [new SpotifyArtist("a1", "Queen", 84, 100, ["rock"])]);

        EnrichCatalogReferencesCommandHandler handler = Build(artists, albums, client);

        await handler.HandleAsync(new EnrichCatalogReferencesCommand());
        // Segunda passada: a1 já está enriquecido, some da fila de pendentes — nada a fazer.
        EnrichCatalogReferencesResult second = await handler.HandleAsync(new EnrichCatalogReferencesCommand());

        Assert.Equal(0, second.ArtistsEnriched);
        Assert.Equal(0, second.ArtistsNotFound);
        Assert.Equal(0, second.ArtistsFailed);
        Assert.Single(artists.Store); // não duplicou o agregado
        Assert.True(artists.Store["a1"].IsEnriched);
    }

    [Fact]
    public async Task ApiOmitsAnId_CountsAsNotFound_NotFailed_AndDoesNotAbortTheBatch()
    {
        var artists = new InMemoryArtistRepository();
        var albums = new InMemoryAlbumRepository();
        SeedArtist(artists, "a1");
        SeedArtist(artists, "a2-desconhecido");

        // A API só conhece a1: a2 fica de fora do retorno em lote (id inválido/removido).
        var client = new FakeEnrichmentClient(
            artists: [new SpotifyArtist("a1", "Queen", 84, 100, ["rock"])]);

        EnrichCatalogReferencesResult result =
            await Build(artists, albums, client).HandleAsync(new EnrichCatalogReferencesCommand());

        Assert.Equal(1, result.ArtistsEnriched);
        // Id não retornado é NotFound (buraco), não Failed (erro) — a métrica não conflaciona os dois.
        Assert.Equal(1, result.ArtistsNotFound);
        Assert.Equal(0, result.ArtistsFailed);
        Assert.True(artists.Store["a1"].IsEnriched);
        // O que a API não retornou segue pendente para a próxima passada — não corrompido —,
        // mas com uma tentativa registrada (anti-starvation).
        Assert.False(artists.Store["a2-desconhecido"].IsEnriched);
        Assert.Equal(1, artists.Store["a2-desconhecido"].EnrichmentAttempts);
    }

    [Fact]
    public async Task PartialFailure_DoesNotAbortTheBatch_WhenOneProfileIsMalformed()
    {
        var artists = new InMemoryArtistRepository();
        var albums = new InMemoryAlbumRepository();
        SeedArtist(artists, "a1");
        SeedArtist(artists, "a2");

        // a2 vem com popularidade fora de 0–100: o VO Popularity rejeita e o agregado a1 segue enriquecido.
        var client = new FakeEnrichmentClient(
            artists:
            [
                new SpotifyArtist("a1", "Queen", 84, 100, ["rock"]),
                new SpotifyArtist("a2", "Bowie", Popularity: 999, Followers: 50, Genres: [])
            ]);

        EnrichCatalogReferencesResult result =
            await Build(artists, albums, client).HandleAsync(new EnrichCatalogReferencesCommand());

        Assert.Equal(1, result.ArtistsEnriched);
        // Perfil malformado é falha REAL (Failed), não NotFound: o id veio, mas o domínio rejeitou o dado.
        Assert.Equal(0, result.ArtistsNotFound);
        Assert.Equal(1, result.ArtistsFailed);
        Assert.True(artists.Store["a1"].IsEnriched);
        Assert.False(artists.Store["a2"].IsEnriched);
        // Uma falha real também registra tentativa: dado cronicamente inválido não fica preso na fila.
        Assert.Equal(1, artists.Store["a2"].EnrichmentAttempts);
    }

    [Fact]
    public async Task DoesNothing_WhenThereAreNoPendingReferences()
    {
        var artists = new InMemoryArtistRepository();
        var albums = new InMemoryAlbumRepository();

        var client = new FakeEnrichmentClient();

        EnrichCatalogReferencesResult result =
            await Build(artists, albums, client).HandleAsync(new EnrichCatalogReferencesCommand());

        Assert.Equal(0, result.TotalEnriched);
        Assert.Equal(0, result.TotalNotFound);
        Assert.Equal(0, result.TotalFailed);
        // Sem pendentes, nem chega a bater na API.
        Assert.Equal(0, client.ArtistBatchCalls);
        Assert.Equal(0, client.AlbumBatchCalls);
    }

    [Fact]
    public async Task Queue_MakesProgress_WhenAPrefixOfIdsIsPermanentlyUnresolvable()
    {
        var artists = new InMemoryArtistRepository();
        var albums = new InMemoryAlbumRepository();

        // Três ids que a API NUNCA retorna, na cabeça da fila (id "a…" ordena antes de "z…"), e um resolvível
        // atrás deles. Sem anti-starvation, com um lote menor que a fila, os irresolúveis ocupariam a cabeça
        // para sempre e "z1" nunca seria alcançado.
        SeedArtist(artists, "a-dead-1");
        SeedArtist(artists, "a-dead-2");
        SeedArtist(artists, "a-dead-3");
        SeedArtist(artists, "z1-resolvivel", "Queen");

        var client = new FakeEnrichmentClient(
            artists: [new SpotifyArtist("z1-resolvivel", "Queen", 84, 100, ["rock"])]);

        EnrichCatalogReferencesCommandHandler handler = Build(artists, albums, client);

        // Lote 2 < 4 pendentes: força a competição pela cabeça da fila. Rodamos ticks suficientes para os três
        // irresolúveis esgotarem MaxEnrichmentAttempts e a fila drenar (teto de segurança generoso; a fila
        // vazia torna os ticks extras no-op).
        const int batchSize = 2;

        // O resolvível é alcançado em POUCOS ticks, não só depois que os dead esgotam — é o progresso que a
        // ordenação por tentativas garante. Sem anti-starvation ele nunca seria alcançado com este lote.
        await handler.HandleAsync(new EnrichCatalogReferencesCommand(batchSize)); // tick 0
        await handler.HandleAsync(new EnrichCatalogReferencesCommand(batchSize)); // tick 1
        Assert.True(artists.Store["z1-resolvivel"].IsEnriched,
            "o artista resolvível deveria ter sido enriquecido em poucos ticks, apesar do prefixo irresolúvel");

        // Deixamos a fila drenar por completo.
        for (int tick = 0; tick < Artist.MaxEnrichmentAttempts * 4; tick++)
            await handler.HandleAsync(new EnrichCatalogReferencesCommand(batchSize));

        // Os irresolúveis saíram da fila (dead-letter), em vez de re-queimar quota indefinidamente.
        foreach (string deadId in new[] { "a-dead-1", "a-dead-2", "a-dead-3" })
        {
            Assert.False(artists.Store[deadId].IsEnriched);
            Assert.Equal(Artist.MaxEnrichmentAttempts, artists.Store[deadId].EnrichmentAttempts);
        }

        // Prova final de que a fila drenou: nada mais pendente e elegível.
        IReadOnlyList<Artist> stillPending = await artists.ListPendingEnrichmentAsync(batchSize);
        Assert.Empty(stillPending);
    }
}
