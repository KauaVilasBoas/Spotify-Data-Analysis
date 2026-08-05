using SpotifyDataAnalysis.Modules.Prediction.Contracts.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.SharedKernel.Exceptions;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;

/// <summary>
/// O caso de uso público do recomendador content-based (E4.2): dada uma faixa-semente do catálogo, devolve as N
/// faixas mais parecidas por cosseno sobre audio-features normalizadas, cada uma com o "porquê rico" — score, as
/// features que mais aproximaram (com valores originais) e o gênero compartilhado.
///
/// <para>É <b>query</b> (leitura pura): consulta o índice cacheado e o catálogo, sem mudar estado. Orquestra três
/// peças já existentes — o motor de similaridade do E4.1 (via <see cref="ITrackSimilarityIndexProvider"/>), a
/// explicabilidade do domínio (<see cref="SimilarityIndex.ExplainNearestTo"/>) e a leitura de metadados do
/// catálogo — sem reimplementar nenhuma.</para>
/// </summary>
/// <param name="SeedTrackId">Id da faixa-semente no catálogo.</param>
/// <param name="Limit">Quantas recomendações retornar (top-N).</param>
/// <param name="ExplainTopK">Quantas features destacar na explicação de cada recomendação (top-K contribuições).</param>
public sealed record GetTrackRecommendationsQuery(string SeedTrackId, int Limit, int ExplainTopK)
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

        IReadOnlyList<ExplainedTrackSimilarity>? neighbors =
            index.ExplainNearestTo(request.SeedTrackId, limit);

        // Semente fora do índice: pode ser inexistente (404) ou existir sem features completas (422). A distinção
        // é a mesma régua do E3.5 e não pode ser engolida — é decidida consultando o catálogo.
        if (neighbors is null)
            throw await ExplainMissingSeedAsync(request.SeedTrackId, cancellationToken);

        TrackMetadataRow? seedMetadata =
            await _metadataSource.FindByTrackIdAsync(request.SeedTrackId, cancellationToken);

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
            Recommendations = recommendations,
            Warnings = BuildWarnings(seedMetadata?.IsImputed ?? false, recommendations)
        };
    }

    /// <summary>
    /// Traduz "semente não está no índice" no erro certo: <see cref="NotFoundException"/> (404) quando o id não
    /// existe no catálogo, <see cref="BusinessException"/> (422) quando existe mas não tem as nove features — sem
    /// insumo não há vetor, e sem vetor não há recomendação.
    /// </summary>
    private async Task<Exception> ExplainMissingSeedAsync(string seedTrackId, CancellationToken cancellationToken)
    {
        TrackMetadataRow? seed = await _metadataSource.FindByTrackIdAsync(seedTrackId, cancellationToken);

        if (seed is null)
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
                IsImputed = neighbor.IsImputed,
                SharedGenre = ResolveSharedGenre(seedGenre, row?.Genre),
                TopFeatures = SelectTopFeatures(neighbor.Contributions, explainTopK)
            });
        }

        return items;
    }

    /// <summary>
    /// O gênero é COMPARTILHADO quando semente e candidata têm o mesmo (comparação ordinal — os gêneros são
    /// gravados normalizados em lowercase pelo Catalog). É informação, não critério de ordenação: o filtro/boost
    /// por gênero no ranking é o E4.3.
    /// </summary>
    private static string? ResolveSharedGenre(string? seedGenre, string? candidateGenre)
    {
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
        bool seedIsImputed, IReadOnlyList<TrackRecommendationItem> recommendations)
    {
        var warnings = new List<string>();

        if (seedIsImputed)
            warnings.Add(SeedImputedWarning);

        if (recommendations.Any(item => item.IsImputed))
            warnings.Add(RecommendationsImputedWarning);

        return warnings;
    }
}
