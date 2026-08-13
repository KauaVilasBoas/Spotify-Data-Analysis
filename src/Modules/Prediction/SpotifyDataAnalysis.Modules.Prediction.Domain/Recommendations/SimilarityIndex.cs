using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

/// <summary>
/// O espaço de similaridade do catálogo: o conjunto de faixas elegíveis já projetadas em vetores NORMALIZADOS
/// (z-score), com os parâmetros de normalização fixados junto. É o motor do recomendador content-based do E4.1 —
/// dado uma semente, devolve as N faixas de maior score, excluindo a própria (kNN top-N em memória, DP-D).
///
/// <para><b>Ranking híbrido (E4.3):</b> o score que ordena deixa de ser cosseno puro e passa a ser o cosseno de
/// áudio COMBINADO com a afinidade de gênero da <see cref="GenreAffinityPolicy"/> — boost aditivo por gênero
/// compartilhado, filtro duro por gênero, ou nada (cosseno puro do E4.1). A política é injetada por consulta; sem
/// política, o ranking é o cosseno puro, então o comportamento do E4.1 continua exatamente aqui dentro. O gênero é
/// aplicado DENTRO da varredura (não num re-rank do top-N já cortado), para o boost poder resgatar uma candidata do
/// mesmo gênero que perderia por pouco no cosseno.</para>
///
/// <para><b>Consistência semente/candidato por construção:</b> os μ/σ são aprendidos UMA vez sobre as faixas
/// cruas que povoam o índice (<see cref="Build"/>), e TODA semente — seja uma faixa do próprio índice, seja um
/// vetor cru externo — é normalizada por esses MESMOS parâmetros antes da comparação. Não há dois caminhos de
/// normalização que possam divergir, que é o que fecha o skew semente/candidato (o análogo do skew
/// treino/inferência do E3.5).</para>
///
/// <para><b>Autoexclusão sempre:</b> a semente nunca aparece no próprio top-N. Quando a semente é uma faixa do
/// índice, ela é excluída por id; quando é um vetor externo, o chamador pode informar um id a excluir.</para>
///
/// <para>Estrutura simples (lista varrida por request) e não um índice ANN: ~90k faixas × 9 doubles é trivial
/// (~7 MB) e a varredura é O(n·d) por consulta. A DP-D manda medir a latência e só pré-computar se p95 > 300 ms;
/// enquanto o número não pedir, esta é a estrutura certa — mais simples e sem aproximação.</para>
/// </summary>
public sealed class SimilarityIndex
{
    private readonly IReadOnlyList<TrackFeatureVector> _entries;
    private readonly Dictionary<string, TrackFeatureVector> _entriesById;

    private SimilarityIndex(
        IReadOnlyList<TrackFeatureVector> entries,
        Dictionary<string, TrackFeatureVector> entriesById,
        FeatureNormalizationParameters normalizationParameters)
    {
        _entries = entries;
        _entriesById = entriesById;
        NormalizationParameters = normalizationParameters;
    }

    /// <summary>Os parâmetros de normalização fixados neste índice — a transformação que vale para toda semente.</summary>
    public FeatureNormalizationParameters NormalizationParameters { get; }

    /// <summary>Quantas faixas o índice cobre. Um índice vazio não pode ser construído.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Constrói o índice a partir das faixas CRUAS elegíveis: aprende μ/σ sobre elas, normaliza cada uma e as
    /// guarda. É a factory que garante a passada única de aprendizado — os mesmos vetores que aprendem os
    /// parâmetros são os que entram no índice, sem chance de o parâmetro descrever um conjunto e o índice outro.
    /// </summary>
    /// <param name="rawTracks">Faixas elegíveis com vetor CRU (unidades originais), gênero e marca de imputação.</param>
    /// <exception cref="DomainException">Quando não há faixa elegível para indexar.</exception>
    public static SimilarityIndex Build(IReadOnlyCollection<RawTrackFeatures> rawTracks)
    {
        ArgumentNullException.ThrowIfNull(rawTracks);

        if (rawTracks.Count == 0)
            throw new DomainException(
                "Não dá para montar o índice de similaridade sem faixas elegíveis: um índice vazio não recomenda nada.");

        var rawVectors = new List<SimilarityFeatureVector>(rawTracks.Count);
        foreach (RawTrackFeatures track in rawTracks)
            rawVectors.Add(track.RawVector);

        FeatureNormalizationParameters parameters = FeatureNormalizationParameters.LearnFrom(rawVectors);

        var entries = new List<TrackFeatureVector>(rawTracks.Count);
        var entriesById = new Dictionary<string, TrackFeatureVector>(rawTracks.Count, StringComparer.Ordinal);

        foreach (RawTrackFeatures track in rawTracks)
        {
            SimilarityFeatureVector normalized = parameters.Normalize(track.RawVector);
            TrackFeatureVector entry = TrackFeatureVector.Create(
                track.TrackId, normalized, track.RawVector, track.Genre, track.IsImputed);

            entries.Add(entry);

            // Última ocorrência vence em caso de id duplicado: o índice não é o dono da unicidade da faixa (o
            // catálogo é), e uma chave duplicada aqui não deve derrubar a montagem do espaço inteiro.
            entriesById[track.TrackId] = entry;
        }

        return new SimilarityIndex(entries, entriesById, parameters);
    }

    /// <summary>Se a faixa está no índice — insumo do modo semente-por-id, que distingue 404 de "existe mas sem features".</summary>
    public bool ContainsTrack(string trackId) =>
        !string.IsNullOrWhiteSpace(trackId) && _entriesById.ContainsKey(trackId);

    /// <summary>O gênero (das audio-features) da faixa no índice, ou null se ela não está no índice ou não tem gênero.</summary>
    public string? GenreOf(string trackId) =>
        _entriesById.TryGetValue(trackId, out TrackFeatureVector? entry) ? entry.Genre : null;

    /// <summary>
    /// Se as features da faixa foram IMPUTADAS (DP-F), ou <c>null</c> quando ela não está no índice. Acessor de
    /// leitura acrescentado pelo E4.4: o ranking já propaga a marca de cada VIZINHO, mas a marca da SEMENTE só
    /// existia fora do índice, e a avaliação precisa tratar semente imputada explicitamente.
    /// </summary>
    public bool? IsImputedTrack(string trackId) =>
        _entriesById.TryGetValue(trackId, out TrackFeatureVector? entry) ? entry.IsImputed : null;

    /// <summary>
    /// As N faixas mais parecidas com a faixa-semente do índice, por CosineSimilarity puro (E4.1), em ordem
    /// decrescente e SEM a própria semente. Sobrecarga de conveniência sem gênero: equivale a passar uma política
    /// neutra. Devolve <c>null</c> quando a semente não está no índice — a distinção entre "faixa inexistente/sem
    /// features" e "faixa sem vizinhos" é do chamador, não engolida aqui.
    /// </summary>
    public IReadOnlyList<TrackSimilarity>? FindNearestTo(string seedTrackId, int topN) =>
        FindNearestTo(seedTrackId, topN, GenreAffinityPolicy.CosineOnly());

    /// <summary>
    /// As N faixas mais parecidas com a faixa-semente do índice sob a <paramref name="genrePolicy"/> (E4.3): o
    /// ranking é o híbrido cosseno + gênero (boost/filtro/nada, conforme a política), em ordem decrescente e SEM a
    /// própria semente. Devolve <c>null</c> quando a semente não está no índice (mesma semântica da sobrecarga sem
    /// gênero: o chamador distingue 404 de "sem vizinhos").
    /// </summary>
    public IReadOnlyList<TrackSimilarity>? FindNearestTo(
        string seedTrackId, int topN, GenreAffinityPolicy genrePolicy)
    {
        ArgumentNullException.ThrowIfNull(genrePolicy);

        if (!_entriesById.TryGetValue(seedTrackId, out TrackFeatureVector? seed))
            return null;

        return RankNeighbors(seed.Vector, topN, seedTrackId, genrePolicy);
    }

    /// <summary>
    /// As N faixas mais parecidas com a semente do índice sob a <paramref name="genrePolicy"/>, cada uma com o
    /// "porquê rico" híbrido do E4.2/E4.3: além do score, a DECOMPOSIÇÃO em cosseno de áudio (esmiuçado por
    /// dimensão) e bônus de gênero. Reusa o MESMO ranking de <see cref="FindNearestTo(string,int,GenreAffinityPolicy)"/>
    /// e apenas o enriquece — o ranking não muda por ser explicado. Devolve <c>null</c> quando a semente não está
    /// no índice.
    ///
    /// <para>As contribuições do cosseno saem de <see cref="CosineSimilarity.ContributionsBetween"/> sobre os
    /// vetores NORMALIZADOS já guardados (nada de recomputar normalização), e os valores originais de cada feature
    /// vêm dos vetores CRUS que a entrada carrega — a explicação é a decomposição exata do próprio score que
    /// ordena, agora somando também o bônus de gênero.</para>
    /// </summary>
    public IReadOnlyList<ExplainedTrackSimilarity>? ExplainNearestTo(
        string seedTrackId, int topN, GenreAffinityPolicy genrePolicy)
    {
        ArgumentNullException.ThrowIfNull(genrePolicy);

        if (!_entriesById.TryGetValue(seedTrackId, out TrackFeatureVector? seed))
            return null;

        IReadOnlyList<TrackSimilarity> ranked = RankNeighbors(seed.Vector, topN, seedTrackId, genrePolicy);

        var explained = new List<ExplainedTrackSimilarity>(ranked.Count);
        foreach (TrackSimilarity neighbor in ranked)
        {
            TrackFeatureVector candidate = _entriesById[neighbor.TrackId];
            explained.Add(new ExplainedTrackSimilarity(
                neighbor.TrackId,
                neighbor.Similarity,
                neighbor.CosineSimilarity,
                neighbor.GenreBonus,
                neighbor.GenreBonus > 0,
                neighbor.IsImputed,
                DescribeContributions(seed, candidate)));
        }

        return explained;
    }

    /// <summary>
    /// Monta a contribuição de cada feature ao COSSENO entre <paramref name="seed"/> e <paramref name="candidate"/>:
    /// a parcela normalizada (de <see cref="CosineSimilarity.ContributionsBetween"/>) casada com os valores
    /// ORIGINAIS de ambos os lados, na ordem canônica das features. Uma feature por posição — o chamador escolhe
    /// as top-K a exibir. É só a parte de ÁUDIO do score; o bônus de gênero viaja à parte no
    /// <see cref="ExplainedTrackSimilarity"/>.
    /// </summary>
    private static IReadOnlyList<FeatureContribution> DescribeContributions(
        TrackFeatureVector seed, TrackFeatureVector candidate)
    {
        IReadOnlyList<double> contributions =
            CosineSimilarity.ContributionsBetween(seed.Vector, candidate.Vector);

        var described = new List<FeatureContribution>(contributions.Count);
        foreach (SimilarityFeature feature in SimilarityFeatures.Ordered)
        {
            int index = (int)feature;
            described.Add(new FeatureContribution(
                feature,
                seed.RawVector[feature],
                candidate.RawVector[feature],
                contributions[index]));
        }

        return described;
    }

    /// <summary>
    /// As N faixas mais parecidas com um vetor CRU externo (semente que não está no catálogo — o modo "features à
    /// mão"), por cosseno puro. O vetor é normalizado pelos MESMOS parâmetros do índice antes de comparar. Sem
    /// gênero: uma semente externa não traz gênero conhecido, então o híbrido não se aplica — é sempre cosseno puro.
    /// Um <paramref name="excludeTrackId"/> opcional autoexclui, caso a semente externa corresponda a uma faixa
    /// conhecida.
    /// </summary>
    public IReadOnlyList<TrackSimilarity> FindNearestTo(
        SimilarityFeatureVector rawSeedVector, int topN, string? excludeTrackId = null)
    {
        ArgumentNullException.ThrowIfNull(rawSeedVector);

        SimilarityFeatureVector normalizedSeed = NormalizationParameters.Normalize(rawSeedVector);

        return RankNeighbors(normalizedSeed, topN, excludeTrackId, GenreAffinityPolicy.CosineOnly());
    }

    /// <summary>
    /// Varre o índice, calcula o score HÍBRIDO de cada candidato (cosseno da semente já NORMALIZADA + bônus de
    /// gênero da <paramref name="genrePolicy"/>) e retém os N maiores, respeitando a elegibilidade da política (o
    /// filtro duro exclui quem não compartilha o gênero da semente). Usa um heap mínimo de tamanho N (seleção
    /// parcial O(n·log N)) em vez de ordenar todo o catálogo: para topN pequeno e n ~90k, ordenar tudo
    /// desperdiçaria a maior parte do trabalho a cada request.
    /// </summary>
    private IReadOnlyList<TrackSimilarity> RankNeighbors(
        SimilarityFeatureVector normalizedSeed, int topN, string? excludeTrackId, GenreAffinityPolicy genrePolicy)
    {
        if (topN <= 0)
            throw new DomainException($"O número de vizinhos pedido deve ser positivo. Recebido: {topN}.");

        var topNeighbors = new TopNeighborHeap(topN);

        foreach (TrackFeatureVector candidate in _entries)
        {
            if (excludeTrackId is not null && string.Equals(candidate.TrackId, excludeTrackId, StringComparison.Ordinal))
                continue;

            if (!genrePolicy.IsCandidateEligible(candidate.Genre, candidate.IsImputed))
                continue;

            double cosine = CosineSimilarity.Between(normalizedSeed, candidate.Vector);
            double genreBonus = genrePolicy.GenreBonusFor(candidate.Genre, candidate.IsImputed);

            topNeighbors.Offer(new TrackSimilarity(
                candidate.TrackId, cosine + genreBonus, cosine, genreBonus, candidate.IsImputed));
        }

        return topNeighbors.ToDescending();
    }
}

/// <summary>
/// A faixa como o índice a recebe para montagem: identidade, vetor CRU (unidades originais, ainda não
/// normalizado), gênero e a marca de imputação. Separa a leitura crua do catálogo da entrada já normalizada do
/// índice — só <see cref="SimilarityIndex.Build"/> conhece a transformação entre as duas.
/// </summary>
/// <param name="TrackId">Identidade da faixa, por valor.</param>
/// <param name="RawVector">O vetor de nove features em unidades originais, na ordem canônica.</param>
/// <param name="Genre">Gênero da faixa (audio-features), já normalizado pelo Catalog em lowercase, ou null.</param>
/// <param name="IsImputed">Se as features foram imputadas, não medidas (DP-F).</param>
public sealed record RawTrackFeatures(
    string TrackId, SimilarityFeatureVector RawVector, string? Genre, bool IsImputed);
