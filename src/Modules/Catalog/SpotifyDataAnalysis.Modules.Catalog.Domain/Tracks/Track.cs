using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks.Events;
using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Guards;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

/// <summary>
/// Agregado raiz de uma <b>faixa</b> do catálogo. Reúne os dados de catálogo/popularidade (da API do
/// Spotify) e, opcionalmente, os <see cref="Tracks.AudioFeatures"/> (do dataset externo). Referências a
/// álbum e artistas são guardadas <b>por valor</b> (ids), sem navegação cross-agregado.
///
/// Mutação apenas pelos métodos de comportamento — nunca por setters públicos (domínio rico).
/// </summary>
public sealed class Track : AggregateRoot<SpotifyTrackId>
{
    private readonly List<TrackArtist> _artists;

    private Track(
        SpotifyTrackId id, string name, Popularity popularity, int durationMs, bool @explicit,
        string? albumId, List<TrackArtist> artists) : base(id)
    {
        Name = name;
        Popularity = popularity;
        DurationMs = durationMs;
        Explicit = @explicit;
        AlbumId = albumId;
        _artists = artists;
    }

    // Construtor sem parâmetros para a materialização do EF Core: a hidratação sobrescreve Id/propriedades/
    // campo via setters e backing fields; os valores abaixo são só placeholders para satisfazer o não-nulo.
    private Track() : base(SpotifyTrackId.Of("_"))
    {
        Name = string.Empty;
        Popularity = Popularity.Unknown;
        _artists = [];
    }

    public string Name { get; private set; }
    public Popularity Popularity { get; private set; }
    public int DurationMs { get; private set; }
    public bool Explicit { get; private set; }

    /// <summary>Id do álbum no Spotify (por valor; sem FK/navegação cross-agregado).</summary>
    public string? AlbumId { get; private set; }

    /// <summary>Créditos de artista da faixa (id por valor + nome), na ordem devolvida pela API.</summary>
    public IReadOnlyList<TrackArtist> Artists => _artists.AsReadOnly();

    /// <summary>Ids dos artistas no Spotify (por valor), derivados dos créditos.</summary>
    public IReadOnlyList<string> ArtistIds => _artists.Select(artist => artist.Id).ToList().AsReadOnly();

    /// <summary>Artista principal (o primeiro crédito); nulo quando a faixa veio sem artistas.</summary>
    public TrackArtist? PrimaryArtist => _artists.Count == 0 ? null : _artists[0];

    /// <summary>Atributos de áudio (anexados a partir do dataset externo); nulos até serem casados.</summary>
    public AudioFeatures? AudioFeatures { get; private set; }

    /// <summary>Registra uma nova faixa no catálogo (named constructor), validando as invariantes.</summary>
    public static Track Register(
        SpotifyTrackId id, string name, Popularity popularity, int durationMs, bool @explicit,
        string? albumId, IEnumerable<TrackArtist> artists)
    {
        Guard.AgainstNull(id, nameof(id));
        Guard.AgainstNull(popularity, nameof(popularity));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstNegative(durationMs, nameof(durationMs));

        string trimmedName = name.Trim();
        var track = new Track(id, trimmedName, popularity, durationMs, @explicit, albumId,
            artists?.ToList() ?? []);

        track.RaiseDomainEvent(new TrackRegisteredDomainEvent(id.Value, trimmedName, popularity.Value, DateTime.UtcNow));
        return track;
    }

    /// <summary>
    /// Reaplica sobre a faixa já catalogada o retrato mais recente vindo da API. É o caminho da <b>reingestão
    /// idempotente</b> (E1.6): popularidade varia com o tempo, faixas são renomeadas e créditos de artista
    /// mudam, mas o registro continua sendo o mesmo agregado.
    ///
    /// Não emite <c>TrackRegistered</c> — a faixa não é nova; emitir de novo faria os consumidores do Outbox
    /// reprocessarem a cada ciclo de coleta.
    /// </summary>
    public void RefreshFromSource(
        string name, Popularity popularity, int durationMs, bool @explicit,
        string? albumId, IEnumerable<TrackArtist> artists)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstNull(popularity, nameof(popularity));
        Guard.AgainstNegative(durationMs, nameof(durationMs));

        Name = name.Trim();
        Popularity = popularity;
        DurationMs = durationMs;
        Explicit = @explicit;
        AlbumId = albumId;

        _artists.Clear();
        _artists.AddRange(artists ?? []);
    }

    /// <summary>Anexa (ou substitui) os atributos de áudio da faixa.</summary>
    public void AttachAudioFeatures(AudioFeatures features)
    {
        Guard.AgainstNull(features, nameof(features));
        AudioFeatures = features;
    }
}
