namespace SpotifyDataAnalysis.Modules.Prediction.Contracts.Recommendations;

/// <summary>
/// A resposta pública do recomendador content-based (E4.2), na fronteira do módulo. POCO achatado: nenhum tipo de
/// domínio (<c>SimilarityIndex</c>, <c>TrackSimilarity</c>) nem do Catalog atravessa aqui — é o contrato que a tela
/// de Recomendações (E5.4) e o Swagger consomem.
///
/// <para>Ecoa a semente (id, nome, gênero, marca de imputação) e devolve as faixas mais parecidas já ordenadas por
/// score decrescente, sem a própria semente. A explicabilidade ("porquê rico") viaja em cada item, não num bloco à
/// parte, para a tela poder mostrar o motivo ao lado da faixa.</para>
/// </summary>
public sealed class TrackRecommendationsResponse
{
    /// <summary>Id da faixa-semente pedida, ecoado.</summary>
    public string SeedTrackId { get; init; } = string.Empty;

    /// <summary>Nome da faixa-semente (do schema <c>catalog</c>), para a tela não precisar de outra ida ao banco.</summary>
    public string? SeedName { get; init; }

    /// <summary>Artista principal da semente (1º crédito), quando disponível.</summary>
    public string? SeedArtist { get; init; }

    /// <summary>Gênero da semente (das audio-features), quando a faixa tem features. Base do <c>SharedGenre</c> dos itens.</summary>
    public string? SeedGenre { get; init; }

    /// <summary>
    /// Se as audio-features da SEMENTE foram imputadas, não medidas (DP-F). Quando <c>true</c>, toda recomendação
    /// parte de um insumo estimado — sinalizado aqui e reforçado em <see cref="Warnings"/>. Imputado nunca passa
    /// por medido em silêncio.
    /// </summary>
    public bool SeedIsImputed { get; init; }

    /// <summary>Quantas faixas o índice de similaridade cobre — o tamanho do espaço varrido.</summary>
    public int IndexedTrackCount { get; init; }

    /// <summary>As faixas recomendadas, em ordem decrescente de similaridade, sem a própria semente. Pode vir vazia.</summary>
    public IReadOnlyList<TrackRecommendationItem> Recommendations { get; init; } = [];

    /// <summary>
    /// Avisos de qualidade sobre esta resposta (ex.: semente imputada, alguma recomendação imputada). Lista vazia
    /// significa recomendações sobre insumo medido — o caso limpo.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Uma faixa recomendada com o "porquê rico" (DP-G do E4.2): identidade e metadados baratos (nome, artista, álbum,
/// gênero), o <see cref="Score"/> de similaridade (cosine, tipicamente 0–1 no espaço z-score), as
/// <see cref="TopFeatures"/> que mais aproximaram a candidata da semente e o <see cref="SharedGenre"/> quando
/// semente e candidata compartilham o gênero.
/// </summary>
public sealed class TrackRecommendationItem
{
    /// <summary>Id da faixa recomendada no Spotify (<c>catalog.tracks.id</c>).</summary>
    public string TrackId { get; init; } = string.Empty;

    /// <summary>Nome da faixa recomendada.</summary>
    public string? Name { get; init; }

    /// <summary>Artista principal da faixa recomendada (1º crédito do array jsonb <c>artists</c>).</summary>
    public string? Artist { get; init; }

    /// <summary>Nome do álbum da faixa recomendada, quando registrado no catálogo.</summary>
    public string? Album { get; init; }

    /// <summary>Gênero das audio-features da faixa recomendada, quando presente.</summary>
    public string? Genre { get; init; }

    /// <summary>Similaridade de cosseno entre a semente e esta faixa — o score que ordena o ranking.</summary>
    public double Score { get; init; }

    /// <summary>
    /// Se as audio-features desta faixa foram imputadas, não medidas (DP-F). A faixa é candidata legítima, mas o
    /// consumidor precisa saber que a similaridade veio de um valor estimado.
    /// </summary>
    public bool IsImputed { get; init; }

    /// <summary>
    /// O gênero COMPARTILHADO com a semente, quando ambos têm o mesmo (senão nulo). É só informação/explicação —
    /// NÃO afeta a ordenação (filtro/boost por gênero é o E4.3).
    /// </summary>
    public string? SharedGenre { get; init; }

    /// <summary>
    /// As K features que mais aproximaram esta faixa da semente, da maior contribuição para a menor. Cada uma traz
    /// os valores originais da semente e da candidata, para o número ser legível.
    /// </summary>
    public IReadOnlyList<FeatureContributionDto> TopFeatures { get; init; } = [];
}

/// <summary>
/// Uma feature que aproximou a candidata da semente, na explicação (E4.2). <see cref="Contribution"/> é a parcela
/// desta feature na soma do cosseno (adimensional); <see cref="SeedValue"/>/<see cref="CandidateValue"/> são os
/// valores ORIGINAIS (unidades do catálogo), sem os quais a contribuição não diz nada ao usuário.
/// </summary>
public sealed class FeatureContributionDto
{
    /// <summary>Nome da feature (ex.: <c>Energy</c>, <c>Danceability</c>).</summary>
    public string Feature { get; init; } = string.Empty;

    /// <summary>Valor original da feature na semente.</summary>
    public double SeedValue { get; init; }

    /// <summary>Valor original da feature na candidata.</summary>
    public double CandidateValue { get; init; }

    /// <summary>Parcela desta feature na soma do cosseno — quanto maior, mais aproximou.</summary>
    public double Contribution { get; init; }
}
