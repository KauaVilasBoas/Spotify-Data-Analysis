using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;
using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Guards;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Artists;

/// <summary>
/// Agregado raiz de um <b>artista</b> do catálogo.
///
/// A ingestão descobre artistas em <b>dois momentos e com profundidades diferentes</b>: ao percorrer as
/// faixas de uma playlist só chegam <i>referências</i> (id + nome), enquanto o endpoint <c>/artists/{id}</c>
/// traz o perfil completo (popularidade, seguidores, gêneros). O agregado modela isso explicitamente com
/// <see cref="RegisterFromReference"/> + <see cref="EnrichProfile"/> e a flag <see cref="IsEnriched"/>, em
/// vez de fingir que um artista recém-descoberto tem popularidade 0 — a distinção importa para o modelo de
/// ML (E3), que usa popularidade/seguidores do artista como feature e precisa saber o que é dado real.
/// </summary>
public sealed class Artist : AggregateRoot<SpotifyArtistId>
{
    private readonly List<string> _genres;

    private Artist(SpotifyArtistId id, string name, Popularity popularity, int followers,
        List<string> genres, bool isEnriched) : base(id)
    {
        Name = name;
        Popularity = popularity;
        Followers = followers;
        _genres = genres;
        IsEnriched = isEnriched;
    }

    // Construtor sem parâmetros para a materialização do EF Core; a hidratação sobrescreve tudo.
    private Artist() : base(SpotifyArtistId.Of("_"))
    {
        Name = string.Empty;
        Popularity = Popularity.Unknown;
        _genres = [];
    }

    public string Name { get; private set; }

    /// <summary>Popularidade do artista (0–100). <see cref="Popularity.Unknown"/> enquanto não enriquecido.</summary>
    public Popularity Popularity { get; private set; }

    /// <summary>Total de seguidores. Zero enquanto não enriquecido.</summary>
    public int Followers { get; private set; }

    /// <summary>Gêneros declarados pelo Spotify para o artista (vazio enquanto não enriquecido).</summary>
    public IReadOnlyList<string> Genres => _genres.AsReadOnly();

    /// <summary>
    /// <see langword="true"/> quando o perfil completo já foi carregado do endpoint de artistas.
    /// Enquanto <see langword="false"/>, popularidade/seguidores/gêneros são <b>ausência de dado</b>, não zero real.
    /// </summary>
    public bool IsEnriched { get; private set; }

    /// <summary>
    /// Registra um artista a partir da referência (id + nome) que vem embutida numa faixa. É o caminho da
    /// ingestão de playlist, que não gasta uma chamada por artista.
    /// </summary>
    public static Artist RegisterFromReference(SpotifyArtistId id, string name)
    {
        Guard.AgainstNull(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));

        return new Artist(id, name.Trim(), Popularity.Unknown, followers: 0, genres: [], isEnriched: false);
    }

    /// <summary>
    /// Completa o perfil com os dados do endpoint <c>/artists/{id}</c>. Idempotente: reexecutar apenas
    /// atualiza os valores (popularidade e seguidores mudam com o tempo).
    /// </summary>
    public void EnrichProfile(string name, Popularity popularity, int followers, IEnumerable<string> genres)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstNull(popularity, nameof(popularity));
        Guard.AgainstNegative(followers, nameof(followers));

        Name = name.Trim();
        Popularity = popularity;
        Followers = followers;

        _genres.Clear();
        _genres.AddRange(genres?.Where(genre => !string.IsNullOrWhiteSpace(genre))
            .Select(genre => genre.Trim()) ?? []);

        IsEnriched = true;
    }

    /// <summary>Corrige o nome quando a API o renomeia, sem tocar no restante do perfil.</summary>
    public void Rename(string name)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Name = name.Trim();
    }
}
