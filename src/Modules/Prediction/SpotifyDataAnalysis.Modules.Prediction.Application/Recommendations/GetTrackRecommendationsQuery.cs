using SpotifyDataAnalysis.Modules.Prediction.Contracts.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Blending;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Deduplication;
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
/// <param name="Dedupe">Se colapsa quase-duplicatas no top-N (E4.7): default true; false devolve o ranking cru.</param>
/// <param name="Strategy">Content puro (default) ou blend com o colaborativo item-item (E4.6).</param>
/// <param name="BlendWeight">Peso do sinal colaborativo no blend, em [0, 1] (E4.6). Só vale para strategy=blend.</param>
public sealed record GetTrackRecommendationsQuery(
    string SeedTrackId, int Limit, int ExplainTopK, GenreRankingModeContract GenreMode, bool Dedupe,
    RecommendationStrategyContract Strategy, double BlendWeight)
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

    /// <summary>Dedup ligado por default (E4.7): o usuário nunca quer ver a mesma música repetida sem pedir.</summary>
    public const bool DefaultDedupe = true;

    /// <summary>Estratégia default (E4.6, DP-2): content puro — o colaborativo é opt-in, sem impor sem evidência.</summary>
    public const RecommendationStrategyContract DefaultStrategy = RecommendationStrategyContract.Content;

    /// <summary>Peso default do colaborativo no blend (E4.6, DP-2): 0,35 (conservador — o content ainda pesa mais).</summary>
    public const double DefaultBlendWeight = 0.35;
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

    private const string CollaborativeUnavailableWarning =
        "O blend colaborativo foi pedido, mas a faixa-semente não tem co-ocorrência registrada em playlists (ou a " +
        "matriz nunca foi construída): as recomendações caíram no content-based puro. O colaborativo não foi " +
        "ignorado em silêncio.";

    /// <summary>
    /// Fator de over-fetch do dedup (E4.7): para entregar <c>limit</c> itens DISTINTOS após colapsar
    /// quase-duplicatas, é preciso pedir mais candidatas do que o limite. 3× cobre com folga o cenário medido no
    /// E4.4 (maior grupo de duplicatas = 54, mas raríssimo no topo de uma semente típica), sem varrer o catálogo
    /// além do necessário — a varredura kNN é O(n) no tamanho do índice, não no over-fetch.
    /// </summary>
    private const int DedupeOverFetchFactor = 3;

    /// <summary>Piso do over-fetch, para limites pequenos ainda terem margem de colapso (ex.: limit=1 pede 10).</summary>
    private const int MinimumDedupeOverFetch = 10;

    private readonly ITrackSimilarityIndexProvider _indexProvider;
    private readonly ITrackMetadataSource _metadataSource;
    private readonly ITrackCoOccurrenceSource _coOccurrenceSource;

    public GetTrackRecommendationsQueryHandler(
        ITrackSimilarityIndexProvider indexProvider,
        ITrackMetadataSource metadataSource,
        ITrackCoOccurrenceSource coOccurrenceSource)
    {
        _indexProvider = indexProvider;
        _metadataSource = metadataSource;
        _coOccurrenceSource = coOccurrenceSource;
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

        // Over-fetch: com dedup (E4.7) ou blend (E4.6), pede-se MAIS candidatas que o limite — colapsar ou blendar
        // reduziria o resultado abaixo do pedido, e o contrato exige entregar `limit` itens distintos. Sem nenhum
        // dos dois, pede-se exatamente `limit` (o comportamento do E4.2/E4.3 permanece intacto).
        bool overFetches = request.Dedupe || request.Strategy == RecommendationStrategyContract.Blend;
        int fetchCount = overFetches
            ? Math.Max(limit * DedupeOverFetchFactor, MinimumDedupeOverFetch)
            : limit;

        IReadOnlyList<ExplainedTrackSimilarity>? neighbors =
            index.ExplainNearestTo(request.SeedTrackId, fetchCount, genrePolicy);

        // Semente fora do índice: pode ser inexistente (404) ou existir sem features completas (422). A distinção
        // é a mesma régua do E3.5 e não pode ser engolida — é decidida a partir do metadata já carregado.
        if (neighbors is null)
            throw ExplainMissingSeed(request.SeedTrackId, seedMetadata);

        // O sinal colaborativo (E4.6) só é buscado no modo blend. Vazio significa cobertura zero (faixa nunca
        // co-ocorreu, ou a matriz nunca foi construída) — o handler cai graciosamente para o content puro.
        IReadOnlyList<CoOccurringTrack> coOccurring = request.Strategy == RecommendationStrategyContract.Blend
            ? await _coOccurrenceSource.FindCoOccurringAsync(request.SeedTrackId, fetchCount, cancellationToken)
            : [];

        RankedCandidate[] ranked = request.Strategy == RecommendationStrategyContract.Blend && coOccurring.Count > 0
            ? BlendCandidates(request.SeedTrackId, neighbors, coOccurring, request.BlendWeight)
            : ContentCandidates(neighbors);

        bool collaborativeUnavailable =
            request.Strategy == RecommendationStrategyContract.Blend && coOccurring.Count == 0;

        // Metadata de TODOS os candidatos (inclui faixas só-colaborativas que não vêm do índice content).
        IReadOnlyDictionary<string, TrackMetadataRow> candidateMetadata =
            await LoadMetadataAsync(ranked.Select(candidate => candidate.TrackId), cancellationToken);

        (IReadOnlyList<RankedCandidate> representatives, IReadOnlyDictionary<string, int> collapsedCounts) =
            request.Dedupe
                ? Deduplicate(ranked, candidateMetadata, index, limit)
                : (TakeLimit(ranked, limit), EmptyCollapsedCounts);

        IReadOnlyList<TrackRecommendationItem> recommendations = BuildRecommendations(
            representatives, candidateMetadata, seedMetadata?.Genre, explainTopK, collapsedCounts);

        RecommendationStrategyContract effectiveStrategy = collaborativeUnavailable
            ? RecommendationStrategyContract.Content
            : request.Strategy;

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
            EffectiveStrategy = effectiveStrategy,
            CollaborativeSignalUnavailable = collaborativeUnavailable,
            DedupeApplied = request.Dedupe,
            TotalDuplicatesCollapsed = collapsedCounts.Values.Sum(),
            Recommendations = recommendations,
            Warnings = BuildWarnings(
                seedMetadata?.IsImputed ?? false, genrePolicy.FellBackToCosineOnly,
                collaborativeUnavailable, recommendations)
        };
    }

    private static readonly IReadOnlyDictionary<string, int> EmptyCollapsedCounts =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// Um candidato já rankeado, no formato que o dedup e a montagem consomem — o denominador comum entre o content
    /// puro e o blend. Carrega a vizinha rica (quando há sinal de áudio) e os campos colaborativos (quando há
    /// co-ocorrência), para a montagem do item saber qual "porquê" exibir sem reconsultar nada.
    /// </summary>
    private sealed record RankedCandidate(
        string TrackId,
        ExplainedTrackSimilarity? Neighbor,
        RecommendationSignal Signal,
        int CoPlaylists,
        double CoOccurrenceScore,
        bool IsImputed,
        double DedupeCosine);

    /// <summary>Traduz o top-N content puro (E4.1–E4.3) em candidatos rankeados — sinal ContentOnly, sem colaborativo.</summary>
    private static RankedCandidate[] ContentCandidates(IReadOnlyList<ExplainedTrackSimilarity> neighbors)
    {
        var candidates = new RankedCandidate[neighbors.Count];
        for (int i = 0; i < neighbors.Count; i++)
        {
            ExplainedTrackSimilarity neighbor = neighbors[i];
            candidates[i] = new RankedCandidate(
                neighbor.TrackId, neighbor, RecommendationSignal.ContentOnly,
                CoPlaylists: 0, CoOccurrenceScore: 0.0, neighbor.IsImputed, neighbor.CosineSimilarity);
        }

        return candidates;
    }

    /// <summary>
    /// Blenda o content-based com o colaborativo (E4.6) via <see cref="RecommendationBlender"/> e traduz o ranking
    /// blendado em candidatos. Faixas só-colaborativas entram sem vizinha rica; o cosseno de dedup delas é 0 (não
    /// têm vetor comparável no top-N de áudio), então só colapsam por chave textual — o que é correto.
    /// </summary>
    private static RankedCandidate[] BlendCandidates(
        string seedTrackId,
        IReadOnlyList<ExplainedTrackSimilarity> neighbors,
        IReadOnlyList<CoOccurringTrack> coOccurring,
        double blendWeight)
    {
        var contentCandidates = new List<BlendContentCandidate>(neighbors.Count);
        foreach (ExplainedTrackSimilarity neighbor in neighbors)
            contentCandidates.Add(new BlendContentCandidate(neighbor.TrackId, neighbor.CosineSimilarity, neighbor));

        var collaborativeCandidates = new List<BlendCollaborativeCandidate>(coOccurring.Count);
        foreach (CoOccurringTrack track in coOccurring)
            collaborativeCandidates.Add(new BlendCollaborativeCandidate(track.TrackId, track.CoPlaylists, track.Jaccard));

        var blender = new RecommendationBlender(blendWeight);
        IReadOnlyList<BlendedRecommendation> blended = blender.Blend(
            seedTrackId, contentCandidates, collaborativeCandidates, int.MaxValue);

        var candidates = new RankedCandidate[blended.Count];
        for (int i = 0; i < blended.Count; i++)
        {
            BlendedRecommendation item = blended[i];
            candidates[i] = new RankedCandidate(
                item.TrackId,
                item.Neighbor,
                item.Signal,
                item.CoPlaylists,
                item.Jaccard,
                item.Neighbor?.IsImputed ?? false,
                item.Neighbor?.CosineSimilarity ?? 0.0);
        }

        return candidates;
    }

    private static IReadOnlyList<RankedCandidate> TakeLimit(RankedCandidate[] candidates, int limit) =>
        candidates.Length <= limit ? candidates : candidates[..limit];

    /// <summary>
    /// Colapsa quase-duplicatas do ranking over-fetched (E4.7) — vale tanto para o content puro quanto para o blend
    /// (o card manda o dedup rodar sobre o ranking final). Monta os <see cref="DeduplicationCandidate"/> a partir do
    /// metadata (chave "artista|título" + popularity) e delega ao <see cref="NearDuplicateCollapser"/>, que funde por
    /// cosseno (o <c>DedupeCosine</c> de cada candidato, via índice) OU pela chave textual.
    /// </summary>
    private static (IReadOnlyList<RankedCandidate> Representatives, IReadOnlyDictionary<string, int> Collapsed)
        Deduplicate(
            RankedCandidate[] ranked,
            IReadOnlyDictionary<string, TrackMetadataRow> metadata,
            SimilarityIndex index,
            int limit)
    {
        var byTrackId = new Dictionary<string, RankedCandidate>(ranked.Length, StringComparer.Ordinal);
        var deduplicationCandidates = new List<DeduplicationCandidate>(ranked.Length);

        foreach (RankedCandidate candidate in ranked)
        {
            metadata.TryGetValue(candidate.TrackId, out TrackMetadataRow? row);
            byTrackId[candidate.TrackId] = candidate;

            // A vizinha rica pode ser null (faixa só-colaborativa): fabrica-se uma casca com o TrackId e a marca de
            // imputação apenas para o colapsador ter uma identidade — a explicabilidade de áudio dessas faixas é
            // vazia por definição, e o dedup só precisa do id e da chave.
            ExplainedTrackSimilarity carrier = candidate.Neighbor
                ?? new ExplainedTrackSimilarity(candidate.TrackId, 0.0, 0.0, 0.0, false, candidate.IsImputed, []);

            deduplicationCandidates.Add(new DeduplicationCandidate(
                carrier,
                RecommendationDuplicateKey.From(row?.Name, row?.Artist),
                row?.Popularity,
                candidate.IsImputed));
        }

        // O cosseno de dedup vem do índice para faixas com vetor; faixas só-colaborativas não estão no índice content
        // e recebem 0 (nunca fundem por cosseno — só por chave, o que é correto).
        var collapser = new NearDuplicateCollapser((a, b) => index.CosineBetween(a, b) ?? 0.0);

        IReadOnlyList<CollapsedRecommendation> collapsed = collapser.Collapse(deduplicationCandidates, limit);

        var representatives = new List<RankedCandidate>(collapsed.Count);
        var collapsedCounts = new Dictionary<string, int>(collapsed.Count, StringComparer.Ordinal);
        foreach (CollapsedRecommendation item in collapsed)
        {
            // O representante preserva os sinais colaborativos do candidato original de mesmo id.
            representatives.Add(byTrackId[item.Representative.TrackId]);
            collapsedCounts[item.Representative.TrackId] = item.CollapsedDuplicateCount;
        }

        return (representatives, collapsedCounts);
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

    private async Task<IReadOnlyDictionary<string, TrackMetadataRow>> LoadMetadataAsync(
        IEnumerable<string> trackIds, CancellationToken cancellationToken)
    {
        string[] ids = trackIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
            return new Dictionary<string, TrackMetadataRow>(StringComparer.Ordinal);

        return await _metadataSource.FindByTrackIdsAsync(ids, cancellationToken);
    }

    private static IReadOnlyList<TrackRecommendationItem> BuildRecommendations(
        IReadOnlyList<RankedCandidate> candidates,
        IReadOnlyDictionary<string, TrackMetadataRow> metadata,
        string? seedGenre,
        int explainTopK,
        IReadOnlyDictionary<string, int> collapsedCounts)
    {
        var items = new List<TrackRecommendationItem>(candidates.Count);

        foreach (RankedCandidate candidate in candidates)
        {
            metadata.TryGetValue(candidate.TrackId, out TrackMetadataRow? row);
            collapsedCounts.TryGetValue(candidate.TrackId, out int collapsed);

            ExplainedTrackSimilarity? neighbor = candidate.Neighbor;

            items.Add(new TrackRecommendationItem
            {
                TrackId = candidate.TrackId,
                Name = row?.Name,
                Artist = row?.Artist,
                Album = row?.Album,
                Genre = row?.Genre,
                // Faixa só-colaborativa não tem score de áudio: o Score exibido é o Jaccard (o único sinal que a
                // sustentou), e cosine/genreBoost ficam zerados — coerente com signal=collaborative.
                Score = neighbor?.Similarity ?? candidate.CoOccurrenceScore,
                CosineScore = neighbor?.CosineSimilarity ?? 0.0,
                GenreBoost = neighbor?.GenreBonus ?? 0.0,
                IsImputed = candidate.IsImputed,
                SharedGenre = neighbor is null ? null : ResolveSharedGenre(neighbor, seedGenre, row?.Genre),
                TopFeatures = neighbor is null ? [] : SelectTopFeatures(neighbor.Contributions, explainTopK),
                EquivalentVersionsCollapsed = collapsed,
                Signal = MapSignalLabel(candidate.Signal),
                CoPlaylists = candidate.CoPlaylists,
                CoOccurrenceScore = candidate.CoOccurrenceScore
            });
        }

        return items;
    }

    /// <summary>O rótulo textual do sinal para o response; nulo no content puro (não há blend a explicar).</summary>
    private static string? MapSignalLabel(RecommendationSignal signal) => signal switch
    {
        RecommendationSignal.CollaborativeOnly => "collaborative",
        RecommendationSignal.Blended => "blended",
        _ => null
    };

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
        bool collaborativeUnavailable,
        IReadOnlyList<TrackRecommendationItem> recommendations)
    {
        var warnings = new List<string>();

        if (seedIsImputed)
            warnings.Add(SeedImputedWarning);

        if (genreFellBackToCosineOnly)
            warnings.Add(GenreFallbackWarning);

        if (collaborativeUnavailable)
            warnings.Add(CollaborativeUnavailableWarning);

        if (recommendations.Any(item => item.IsImputed))
            warnings.Add(RecommendationsImputedWarning);

        return warnings;
    }
}
