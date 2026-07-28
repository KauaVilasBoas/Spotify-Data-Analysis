using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Guards;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Albums;

/// <summary>
/// Agregado raiz de um <b>álbum</b> do catálogo.
///
/// Assim como <c>Artist</c>, chega em dois níveis de profundidade: a referência (id + nome) embutida na
/// faixa durante a ingestão de playlist e o detalhe completo (data de lançamento, total de faixas) do
/// endpoint <c>/albums/{id}</c>. <see cref="IsEnriched"/> separa "ainda não sabemos" de "é zero mesmo".
/// </summary>
public sealed class Album : AggregateRoot<SpotifyAlbumId>
{
    private Album(SpotifyAlbumId id, string name, ReleaseDate? releaseDate, int totalTracks, bool isEnriched)
        : base(id)
    {
        Name = name;
        ReleaseDate = releaseDate;
        TotalTracks = totalTracks;
        IsEnriched = isEnriched;
    }

    // Construtor sem parâmetros para a materialização do EF Core; a hidratação sobrescreve tudo.
    private Album() : base(SpotifyAlbumId.Of("_")) => Name = string.Empty;

    public string Name { get; private set; }

    /// <summary>Data de lançamento com precisão variável; nula enquanto o álbum não foi enriquecido.</summary>
    public ReleaseDate? ReleaseDate { get; private set; }

    /// <summary>Total de faixas do álbum. Zero enquanto não enriquecido.</summary>
    public int TotalTracks { get; private set; }

    /// <summary><see langword="true"/> quando o detalhe completo já foi carregado do endpoint de álbuns.</summary>
    public bool IsEnriched { get; private set; }

    /// <summary>Registra um álbum a partir da referência (id + nome) embutida numa faixa.</summary>
    public static Album RegisterFromReference(SpotifyAlbumId id, string name)
    {
        Guard.AgainstNull(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));

        return new Album(id, name.Trim(), releaseDate: null, totalTracks: 0, isEnriched: false);
    }

    /// <summary>
    /// Completa o álbum com os dados do endpoint <c>/albums/{id}</c>. O <paramref name="rawReleaseDate"/> é o
    /// texto cru da API — a interpretação da precisão é responsabilidade do value object
    /// <see cref="Albums.ReleaseDate"/>.
    /// </summary>
    public void EnrichDetails(string name, string? rawReleaseDate, int totalTracks)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstNegative(totalTracks, nameof(totalTracks));

        Name = name.Trim();
        ReleaseDate = ReleaseDate.TryParse(rawReleaseDate);
        TotalTracks = totalTracks;
        IsEnriched = true;
    }

    /// <summary>Corrige o nome quando a API o renomeia, sem tocar no restante do detalhe.</summary>
    public void Rename(string name)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Name = name.Trim();
    }
}
