namespace SpotifyDataAnalysis.Modules.Prediction.Contracts.Recommendations;

/// <summary>
/// Como o gênero da semente pesa no ranking (E4.3, DP-C/DP-1) — o eixo de relaxamento exposto no endpoint. É um
/// enum POCO do contrato (nenhum tipo de domínio atravessa a fronteira): o módulo traduz para a política interna.
/// </summary>
public enum GenreRankingModeContract
{
    /// <summary>Gênero como BOOST (default): candidatas do mesmo gênero da semente ganham um bônus no score.</summary>
    Boost = 0,

    /// <summary>Gênero IGNORADO: cai no cosine puro do E4.1 (relaxamento total — "só-áudio").</summary>
    Off = 1,

    /// <summary>Gênero como FILTRO duro: só entram no top-N candidatas do mesmo gênero da semente.</summary>
    SameGenreOnly = 2
}

/// <summary>
/// A resposta pública do recomendador content-based híbrido (E4.2/E4.3), na fronteira do módulo. POCO achatado:
/// nenhum tipo de domínio (<c>SimilarityIndex</c>, <c>TrackSimilarity</c>) nem do Catalog atravessa aqui — é o
/// contrato que a tela de Recomendações (E5.4) e o Swagger consomem.
///
/// <para>Ecoa a semente (id, nome, gênero, marca de imputação) e devolve as faixas mais parecidas já ordenadas por
/// score decrescente, sem a própria semente. A explicabilidade ("porquê rico") viaja em cada item, não num bloco à
/// parte, para a tela poder mostrar o motivo ao lado da faixa. O modo de gênero EFETIVO e o eventual fallback ao
/// cosine puro (semente sem gênero) vêm no envelope, para o consumidor nunca ser degradado em silêncio.</para>
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

    /// <summary>
    /// O modo de gênero PEDIDO pelo cliente (o eixo de relaxamento da DP-C). Ecoado para o consumidor conferir o
    /// que solicitou, mesmo quando o modo efetivo caiu para <see cref="GenreRankingModeContract.Off"/> por fallback.
    /// </summary>
    public GenreRankingModeContract RequestedGenreMode { get; init; }

    /// <summary>
    /// O modo de gênero EFETIVAMENTE aplicado no ranking. Coincide com <see cref="RequestedGenreMode"/>, salvo
    /// quando a semente não tem gênero utilizável — aí o ranking cai no cosine puro
    /// (<see cref="GenreRankingModeContract.Off"/>) e <see cref="GenreFellBackToCosineOnly"/> fica <c>true</c>.
    /// </summary>
    public GenreRankingModeContract EffectiveGenreMode { get; init; }

    /// <summary>
    /// <c>true</c> quando o cliente pediu boost/filtro mas a semente não tinha gênero utilizável (ausente ou
    /// imputado, DP-F) e o ranking caiu no cosine puro. Sinaliza a degradação — o gênero nunca é ignorado em
    /// silêncio, e o motivo também aparece em <see cref="Warnings"/>.
    /// </summary>
    public bool GenreFellBackToCosineOnly { get; init; }

    /// <summary>
    /// Se o dedup de quase-duplicatas (E4.7) foi aplicado a esta resposta. Quando <c>true</c>, o top-N não repete a
    /// mesma música em <c>track_id</c>s diferentes; itens com <see cref="TrackRecommendationItem.EquivalentVersionsCollapsed"/>
    /// &gt; 0 representam um grupo de versões equivalentes. <c>false</c> quando o cliente pediu <c>dedupe=false</c>.
    /// </summary>
    public bool DedupeApplied { get; init; }

    /// <summary>
    /// Quantas quase-duplicatas foram colapsadas no total desta resposta (soma dos
    /// <see cref="TrackRecommendationItem.EquivalentVersionsCollapsed"/>). Zero quando o dedup não achou repetição ou
    /// está desligado — o número que quantifica o quanto o top-N estava "sujo" antes do colapso.
    /// </summary>
    public int TotalDuplicatesCollapsed { get; init; }

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

    /// <summary>
    /// O score HÍBRIDO que ordena o ranking (E4.3): <see cref="CosineScore"/> + <see cref="GenreBoost"/>. Quando o
    /// gênero não pesa (modo off/fallback), coincide com o cosseno — o comportamento do E4.1.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Só a similaridade de cosseno de áudio entre a semente e esta faixa (o eixo do E4.1), SEM o gênero. Exposto ao
    /// lado do <see cref="Score"/> para o boost ser auditável: dá para ver o quanto o gênero moveu esta faixa.
    /// </summary>
    public double CosineScore { get; init; }

    /// <summary>
    /// O bônus somado ao cosseno por esta faixa compartilhar o gênero da semente (0 quando não compartilha ou o
    /// gênero não pesa). É a CONTRIBUIÇÃO do gênero ao ranking — sem ela o boost ficaria invisível (risco do card).
    /// </summary>
    public double GenreBoost { get; init; }

    /// <summary>
    /// Se as audio-features desta faixa foram imputadas, não medidas (DP-F). A faixa é candidata legítima, mas o
    /// consumidor precisa saber que a similaridade veio de um valor estimado — e ela não recebe boost de gênero.
    /// </summary>
    public bool IsImputed { get; init; }

    /// <summary>
    /// O gênero COMPARTILHADO com a semente, quando ambos têm o mesmo (senão nulo). A partir do E4.3, quando o
    /// gênero pesa no ranking, é ele que motiva o <see cref="GenreBoost"/> (ou a sobrevivência ao filtro duro).
    /// </summary>
    public string? SharedGenre { get; init; }

    /// <summary>
    /// As K features que mais aproximaram esta faixa da semente, da maior contribuição para a menor. Cada uma traz
    /// os valores originais da semente e da candidata, para o número ser legível.
    /// </summary>
    public IReadOnlyList<FeatureContributionDto> TopFeatures { get; init; } = [];

    /// <summary>
    /// Quantas OUTRAS versões equivalentes da mesma música (quase-duplicatas com <c>track_id</c>s diferentes) este
    /// item representa, após o dedup do E4.7. Zero significa faixa única no top-N; &gt; 0 é a transparência de que
    /// "esta faixa fala por N versões" — o catálogo tem duplicatas reais, e o item as absorveu em vez de repeti-las.
    /// </summary>
    public int EquivalentVersionsCollapsed { get; init; }
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
