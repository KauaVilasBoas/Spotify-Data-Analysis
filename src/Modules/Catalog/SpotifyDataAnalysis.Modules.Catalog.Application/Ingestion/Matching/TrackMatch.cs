using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Matching;

/// <summary>
/// Como uma linha do dataset externo foi casada com o catálogo. Não é um detalhe de log: a distribuição
/// entre os modos é a <b>métrica de qualidade da ingestão</b> (E1.4) — um fallback textual respondendo pela
/// maioria dos casamentos é sinal de que os ids divergiram e o dado merece desconfiança.
/// </summary>
public enum TrackMatchKind
{
    /// <summary>Nenhuma estratégia encontrou a faixa no catálogo.</summary>
    None = 0,

    /// <summary>Casada pelo <c>track_id</c> do Spotify — casamento exato, o caminho preferencial.</summary>
    SpotifyTrackId = 1,

    /// <summary>
    /// Casada pela chave normalizada "artista + título" <b>confirmada pela duração</b> — a mesma heurística
    /// textual do <see cref="NameAndArtist"/>, mas desambiguando homônimos do mesmo artista pela duração da
    /// gravação (E1.9). Mais confiável que a textual pura porque distingue faixas distintas que colidem na chave.
    /// </summary>
    NameAndDuration = 2,

    /// <summary>Casada pela chave normalizada "artista + título" — heurística, sujeita a colisão.</summary>
    NameAndArtist = 3
}

/// <summary>Resultado de um casamento: a faixa encontrada (se houve) e por qual estratégia.</summary>
public sealed record TrackMatch(Track? Track, TrackMatchKind Kind)
{
    public static TrackMatch NotFound { get; } = new(null, TrackMatchKind.None);

    /// <summary>Casamento bem-sucedido — a faixa está garantidamente preenchida.</summary>
    public bool IsMatch => Track is not null;
}
