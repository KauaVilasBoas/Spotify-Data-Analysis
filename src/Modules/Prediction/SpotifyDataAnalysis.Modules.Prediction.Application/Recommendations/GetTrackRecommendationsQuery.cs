using SpotifyDataAnalysis.Modules.Prediction.Contracts.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.SharedKernel.Exceptions;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;

/// <summary>
/// O caso de uso público do recomendador content-based híbrido (E4.2/E4.3): dada uma faixa-semente do catálogo,
/// devolve as N faixas mais parecidas por um score HÍBRIDO — cosseno sobre audio-features normalizadas combinado
/// com a afinidade de gênero (boost/filtro/nada, conforme <see cref="GenreMode"/>) —, cada uma com o "porquê rico":
/// score, as features que mais aproximaram (com valores originais), o gênero compartilhado e a contribuição do
/// gênero ao ranking.
///
/// <para>É <b>query</b> (leitura pura): consulta o índice cacheado e o catálogo, sem mudar estado. Orquestra as
/// peças já existentes — o motor de similaridade do E4.1 (via <see cref="ITrackSimilarityIndexProvider"/>), a
/// explicabilidade do domínio (<see cref="SimilarityIndex.ExplainNearestTo"/>) e a leitura de metadados do
/// catálogo — e adiciona o estágio de gênero do E4.3 traduzindo o modo pedido numa <see cref="GenreAffinityPolicy"/>.</para>
/// </summary>
/// <param name="SeedTrackId">Id da faixa-semente no catálogo.</param>
/// <param name="Limit">Quantas recomendações retornar (top-N).</param>
/// <param name="ExplainTopK">Quantas features destacar na explicação de cada recomendação (top-K contribuições).</param>
/// <param name="GenreMode">Como o gênero da semente pesa no ranking (E4.3): boost (default), off (cosine puro) ou filtro duro.</param>
public sealed record GetTrackRecommendationsQuery(
    string SeedTrackId, int Limit, int ExplainTopK, GenreRankingModeContract GenreMode)
    : IQuery<TrackRecommendationsResponse>
{
    /// <summary>Número de recomendações padrão quando o cliente não especifica.</summary>
    public const int DefaultLimit = 10;

    /// <summary>Teto de recomendações: um pedido absurdo não vira uma resposta gigante (card: máx 50).</summary>
    public const int MaximumLimit = 50;

    /// <summary>Número de features destacadas por padrão na explicação (DP-1: top-3, legível na tela).</summary>
    public const int DefaultExplainTopK = 3;

    /// <summary>Teto de features destacadas: a dimensão do vetor (não há mais que nove contribuições).</summary>
    public const int MaximumExplainTopK = 9;

    /// <summary>Modo de gênero padrão (DP-C/DP-1): gênero LIGADO como boost — o híbrido leve por default.</summary>
    public const GenreRankingModeContract DefaultGenreMode = GenreRankingModeContract.Boost;
}

internal sealed class GetTrackRecommendationsQueryHandler
    : IQueryHandler<GetTrackRecommendationsQuery, TrackRecommendationsResponse>
{
    private const string SeedImputedWarning =
        "As audio-features da faixa-semente foram IMPUTADAS (preenchidas por estimativa), não medidas — toda " +
        "recomendação parte de um insumo aproximado e deve ser lida com essa ressalva.";

    private const string RecommendationsImputedWarning =
        "Uma ou mais faixas recomendadas têm audio-features IMPUTADAS, não medidas — vêm marcadas com " +
        "isImputed=true e sua similaridade foi calculada sobre um valor estimado.";

    private const string GenreFallbackWarning =
        "A faixa-semente não tem gênero utilizável (ausente ou imputado), então o gênero foi ignorado no ranking: " +
        "as recomendações caíram no cosine puro de audio-features. Nenhuma faixa foi filtrada em silêncio.";

    private readonly ITrackSimilarityIndexProvider _indexProvider;
    private readonly ITrackMetadataSource _metadataSource;

    public GetTrackRecommendationsQueryHandler(
        ITrackSimilarityIndexProvider indexProvider, ITrackMetadataSource metadataSource)
    {
        _indexProvider = indexProvider;
        _metadataSource = metadataSource;
    }

    public async Task<TrackRecommendationsResponse> HandleAsync(
        GetTrackRecommendationsQuery request, CancellationToken cancellationToken = default)
    {
        // Clamp defensivo: o validador de fronteira já barra valores fora de faixa (400), mas o domínio nunca deve
        // receber um topN não-positivo (viraria DomainException → 500). Defesa em profundidade, não repetição.
        int limit = Math.Clamp(request.Limit, 1, GetTrackRecommendationsQuery.MaximumLimit);
        int explainTopK = Math.Clamp(request.ExplainTopK, 1, GetTrackRecommendationsQuery.MaximumExplainTopK);

        SimilarityIndex index = await _indexProvider.GetIndexAsync(cancellationToken);

        // A semente decide 404/422 e alimenta a política de gênero (rótulo + imputação). Lida uma vez, aqui, tanto
        // para o caminho de erro quanto para o de sucesso.
        TrackMetadataRow? seedMetadata =
            await _metadataSource.FindByTrackIdAsync(request.SeedTrackId, cancellationToken);

        // O gênero que EFETIVAMENTE pontua é o do índice (a mesma fonte que o ranking compara candidato a
        // candidato) — não o do metadata, para não haver dois rótulos que possam divergir. A marca de imputação
        // vem do metadata da semente (mesmo jsonb): gênero de faixa imputada é estimado e não deve boostar (DP-F).
        GenreAffinityPolicy genrePolicy = GenreAffinityPolicy.Create(
            MapGenreMode(request.GenreMode),
            index.GenreOf(request.SeedTrackId),
            seedMetadata?.IsImputed ?? false);

        IReadOnlyList<ExplainedTrackSimilarity>? neighbors =
            index.ExplainNearestTo(request.SeedTrackId, limit, genrePolicy);

        // Semente fora do índice: pode ser inexistente (404) ou existir sem features completas (422). A distinção
        // é a mesma régua do E3.5 e não pode ser engolida — é decidida a partir do metadata já carregado.
        if (neighbors is null)
            throw ExplainMissingSeed(request.SeedTrackId, seedMetadata);

        IReadOnlyDictionary<string, TrackMetadataRow> neighborMetadata =
            await LoadNeighborMetadataAsync(neighbors, cancellationToken);

        IReadOnlyList<TrackRecommendationItem> recommendations =
            BuildRecommendations(neighbors, neighborMetadata, seedMetadata?.Genre, explainTopK);

        return new TrackRecommendationsResponse
        {
            SeedTrackId = request.SeedTrackId,
            SeedName = seedMetadata?.Name,
            SeedArtist = seedMetadata?.Artist,
            SeedGenre = seedMetadata?.Genre,
            SeedIsImputed = seedMetadata?.IsImputed ?? false,
            IndexedTrackCount = index.Count,
            RequestedGenreMode = request.GenreMode,
            EffectiveGenreMode = MapGenreMode(genrePolicy.Mode),
            GenreFellBackToCosineOnly = genrePolicy.FellBackToCosineOnly,
            Recommendations = recommendations,
            Warnings = BuildWarnings(
                seedMetadata?.IsImputed ?? false, genrePolicy.FellBackToCosineOnly, recommendations)
        };
    }

    /// <summary>Traduz o modo de gênero do CONTRATO para o do domínio — a fronteira não deixa o enum de domínio vazar.</summary>
    private static GenreRankingMode MapGenreMode(GenreRankingModeContract mode) => mode switch
    {
        GenreRankingModeContract.Off => GenreRankingMode.Off,
        GenreRankingModeContract.SameGenreOnly => GenreRankingMode.SameGenreOnly,
        _ => GenreRankingMode.Boost
    };

    /// <summary>Traduz o modo de gênero do domínio de volta para o CONTRATO, para o response ecoar o modo efetivo.</summary>
    private static GenreRankingModeContract MapGenreMode(GenreRankingMode mode) => mode switch
    {
        GenreRankingMode.Off => GenreRankingModeContract.Off,
        GenreRankingMode.SameGenreOnly => GenreRankingModeContract.SameGenreOnly,
        _ => GenreRankingModeContract.Boost
    };

    /// <summary>
    /// Traduz "semente não está no índice" no erro certo, a partir do metadata JÁ carregado: <see cref="NotFoundException"/>
    /// (404) quando o id não existe no catálogo, <see cref="BusinessException"/> (422) quando existe mas não tem as
    /// nove features — sem insumo não há vetor, e sem vetor não há recomendação. Síncrono porque não relê nada: a
    /// semente foi lida uma vez no início e serve aos dois caminhos.
    /// </summary>
    private static Exception ExplainMissingSeed(string seedTrackId, TrackMetadataRow? seedMetadata)
    {
        if (seedMetadata is null)
            return new NotFoundException("Track", seedTrackId);

        return new BusinessException(
            $"A faixa '{seedTrackId}' não tem audio-features completas no catálogo — sem esse insumo não há " +
            "vetor de similaridade e, portanto, não há como recomendar faixas parecidas.");
    }

    private async Task<IReadOnlyDictionary<string, TrackMetadataRow>> LoadNeighborMetadataAsync(
        IReadOnlyList<ExplainedTrackSimilarity> neighbors, CancellationToken cancellationToken)
    {
        if (neighbors.Count == 0)
            return new Dictionary<string, TrackMetadataRow>(StringComparer.Ordinal);

        string[] ids = neighbors.Select(neighbor => neighbor.TrackId).ToArray();

        return await _metadataSource.FindByTrackIdsAsync(ids, cancellationToken);
    }

    private static IReadOnlyList<TrackRecommendationItem> BuildRecommendations(
        IReadOnlyList<ExplainedTrackSimilarity> neighbors,
        IReadOnlyDictionary<string, TrackMetadataRow> metadata,
        string? seedGenre,
        int explainTopK)
    {
        var items = new List<TrackRecommendationItem>(neighbors.Count);

        foreach (ExplainedTrackSimilarity neighbor in neighbors)
        {
            metadata.TryGetValue(neighbor.TrackId, out TrackMetadataRow? row);

            items.Add(new TrackRecommendationItem
            {
                TrackId = neighbor.TrackId,
                Name = row?.Name,
                Artist = row?.Artist,
                Album = row?.Album,
                Genre = row?.Genre,
                Score = neighbor.Similarity,
                CosineScore = neighbor.CosineSimilarity,
                GenreBoost = neighbor.GenreBonus,
                IsImputed = neighbor.IsImputed,
                SharedGenre = ResolveSharedGenre(neighbor, seedGenre, row?.Genre),
                TopFeatures = SelectTopFeatures(neighbor.Contributions, explainTopK)
            });
        }

        return items;
    }

    /// <summary>
    /// O gênero COMPARTILHADO exibido ao lado da faixa. Quando o gênero PESA no ranking (boost/filtro), a fonte da
    /// verdade é o próprio domínio: <see cref="ExplainedTrackSimilarity.SharesSeedGenre"/> é o que motivou o boost/
    /// a sobrevivência ao filtro, então "sharedGenre setado" ⟺ "gênero contou no ranking" — sem uma comparação
    /// paralela no handler que pudesse divergir do motor (ex.: um rótulo imputado que o domínio não boostou). Quando
    /// o gênero NÃO pesa (modo off/fallback), cai para a comparação descritiva do E4.2, ordinal sobre os rótulos já
    /// normalizados em lowercase pelo Catalog — informação, sem efeito no ranking.
    /// </summary>
    private static string? ResolveSharedGenre(
        ExplainedTrackSimilarity neighbor, string? seedGenre, string? candidateGenre)
    {
        // Gênero pesou no ranking (boost/filtro duro): o domínio é a fonte da verdade do que compartilhou.
        if (neighbor.SharesSeedGenre)
            return candidateGenre ?? seedGenre;

        // Modo off/fallback: nada boostou, mas o gênero compartilhado ainda é informação descritiva (E4.2).
        if (string.IsNullOrWhiteSpace(seedGenre) || string.IsNullOrWhiteSpace(candidateGenre))
            return null;

        return string.Equals(seedGenre, candidateGenre, StringComparison.Ordinal) ? seedGenre : null;
    }

    /// <summary>
    /// As K features de MAIOR contribuição positiva, da maior para a menor — as que mais aproximaram a candidata da
    /// semente. Ordena a decomposição já pronta do domínio (não recalcula nada) e corta em K.
    /// </summary>
    private static IReadOnlyList<FeatureContributionDto> SelectTopFeatures(
        IReadOnlyList<FeatureContribution> contributions, int explainTopK)
    {
        return contributions
            .OrderByDescending(contribution => contribution.Contribution)
            .Take(explainTopK)
            .Select(contribution => new FeatureContributionDto
            {
                Feature = contribution.Feature.ToString(),
                SeedValue = contribution.SeedValue,
                CandidateValue = contribution.CandidateValue,
                Contribution = contribution.Contribution
            })
            .ToArray();
    }

    private static IReadOnlyList<string> BuildWarnings(
        bool seedIsImputed,
        bool genreFellBackToCosineOnly,
        IReadOnlyList<TrackRecommendationItem> recommendations)
    {
        var warnings = new List<string>();

        if (seedIsImputed)
            warnings.Add(SeedImputedWarning);

        if (genreFellBackToCosineOnly)
            warnings.Add(GenreFallbackWarning);

        if (recommendations.Any(item => item.IsImputed))
            warnings.Add(RecommendationsImputedWarning);

        return warnings;
    }
}
