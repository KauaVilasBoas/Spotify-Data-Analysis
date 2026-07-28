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
    private readonly List<string> _artistIds;

    private Track(
        SpotifyTrackId id, string name, Popularity popularity, int durationMs, bool @explicit,
        string? albumId, List<string> artistIds) : base(id)
    {
        Name = name;
        Popularity = popularity;
        DurationMs = durationMs;
        Explicit = @explicit;
        AlbumId = albumId;
        _artistIds = artistIds;
    }

    // Construtor sem parâmetros para a materialização do EF Core: a hidratação sobrescreve Id/propriedades/
    // campo via setters e backing fields; os valores abaixo são só placeholders para satisfazer o não-nulo.
    private Track() : base(SpotifyTrackId.Of("_"))
    {
        Name = string.Empty;
        Popularity = Popularity.Of(0);
        _artistIds = [];
    }

    public string Name { get; private set; }
    public Popularity Popularity { get; private set; }
    public int DurationMs { get; private set; }
    public bool Explicit { get; private set; }

    /// <summary>Id do álbum no Spotify (por valor; sem FK/navegação cross-agregado).</summary>
    public string? AlbumId { get; private set; }

    /// <summary>Ids dos artistas no Spotify (por valor).</summary>
    public IReadOnlyList<string> ArtistIds => _artistIds.AsReadOnly();

    /// <summary>Atributos de áudio (anexados a partir do dataset externo); nulos até serem casados.</summary>
    public AudioFeatures? AudioFeatures { get; private set; }

    /// <summary>Registra uma nova faixa no catálogo (named constructor), validando as invariantes.</summary>
    public static Track Register(
        SpotifyTrackId id, string name, Popularity popularity, int durationMs, bool @explicit,
        string? albumId, IEnumerable<string> artistIds)
    {
        Guard.AgainstNull(id, nameof(id));
        Guard.AgainstNull(popularity, nameof(popularity));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstNegative(durationMs, nameof(durationMs));

        string trimmedName = name.Trim();
        var track = new Track(id, trimmedName, popularity, durationMs, @explicit, albumId,
            artistIds?.ToList() ?? []);

        track.RaiseDomainEvent(new TrackRegisteredDomainEvent(id.Value, trimmedName, popularity.Value, DateTime.UtcNow));
        return track;
    }

    /// <summary>Anexa (ou substitui) os atributos de áudio da faixa.</summary>
    public void AttachAudioFeatures(AudioFeatures features)
    {
        Guard.AgainstNull(features, nameof(features));
        AudioFeatures = features;
    }

    /// <summary>Atualiza a popularidade (reingestão — a popularidade varia com o tempo).</summary>
    public void UpdatePopularity(Popularity popularity)
    {
        Guard.AgainstNull(popularity, nameof(popularity));
        Popularity = popularity;
    }
}
