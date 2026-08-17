using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations;

/// <summary>
/// A normalização z-score (DP-1) e a razão de ela existir: sem ela, o cosine é dominado por Tempo/Loudness
/// (escalas ~0–243 e ~−50–5 contra 0–1) e a similaridade vira lixo; com ela, nenhuma feature domina por escala.
/// Prova os DOIS lados, como o card exige, e a consistência semente/candidato (mesma transformação).
/// </summary>
public sealed class FeatureNormalizationTests
{
    private const double Tolerance = 1e-9;

    /// <summary>
    /// Monta um vetor cru com escalas REALISTAS: as sete features [0,1], Tempo em BPM, Loudness em dB. É a
    /// heterogeneidade medida no catálogo — o que torna a normalização não-opcional.
    /// </summary>
    private static SimilarityFeatureVector Raw(
        double danceability, double energy, double valence, double tempo,
        double acousticness, double instrumentalness, double liveness, double speechiness, double loudness) =>
        SimilarityFeatureVector.Create(
        [
            danceability, energy, valence, tempo, acousticness,
            instrumentalness, liveness, speechiness, loudness
        ]);

    /// <summary>Um catálogo de aprendizado com escalas heterogêneas plausíveis, para μ/σ refletirem a realidade.</summary>
    private static IReadOnlyList<SimilarityFeatureVector> LearningCatalog() =>
    [
        Raw(0.20, 0.30, 0.10,  80.0, 0.90, 0.00, 0.10, 0.03, -20.0),
        Raw(0.50, 0.55, 0.50, 120.0, 0.40, 0.10, 0.15, 0.05, -10.0),
        Raw(0.80, 0.85, 0.90, 160.0, 0.10, 0.30, 0.25, 0.08,  -4.0),
        Raw(0.35, 0.45, 0.30, 100.0, 0.60, 0.05, 0.12, 0.04, -14.0),
        Raw(0.65, 0.75, 0.70, 140.0, 0.20, 0.20, 0.20, 0.06,  -7.0)
    ];

    [Fact]
    public void LearnFrom_EmptyCatalog_Throws()
    {
        Assert.Throws<DomainException>(() => FeatureNormalizationParameters.LearnFrom([]));
    }

    [Fact]
    public void LearnFrom_ComputesMeanAndStdDevPerFeature()
    {
        // Duas faixas: Tempo 100 e 140 → μ=120, σ=20 (populacional). Confere que o parâmetro é POR coluna.
        IReadOnlyList<SimilarityFeatureVector> catalog =
        [
            Raw(0.4, 0.4, 0.4, 100.0, 0.4, 0.0, 0.1, 0.05, -10.0),
            Raw(0.6, 0.6, 0.6, 140.0, 0.6, 0.2, 0.3, 0.05, -20.0)
        ];

        FeatureNormalizationParameters parameters = FeatureNormalizationParameters.LearnFrom(catalog);

        Assert.Equal(120.0, parameters.Means[(int)SimilarityFeature.Tempo], Tolerance);
        Assert.Equal(20.0, parameters.StandardDeviations[(int)SimilarityFeature.Tempo], Tolerance);
    }

    [Fact]
    public void Normalize_ConstantFeature_DoesNotBlowUp()
    {
        // Speechiness constante (0.05) em TODO o catálogo → σ≈0. A guarda troca σ por 1, então a coordenada
        // centrada colapsa para 0 em vez de virar NaN/∞ — uma feature sem variância não distingue ninguém.
        IReadOnlyList<SimilarityFeatureVector> constantSpeechiness =
        [
            Raw(0.20, 0.30, 0.10,  80.0, 0.90, 0.00, 0.10, 0.05, -20.0),
            Raw(0.50, 0.55, 0.50, 120.0, 0.40, 0.10, 0.15, 0.05, -10.0),
            Raw(0.80, 0.85, 0.90, 160.0, 0.10, 0.30, 0.25, 0.05,  -4.0)
        ];
        FeatureNormalizationParameters parameters = FeatureNormalizationParameters.LearnFrom(constantSpeechiness);

        SimilarityFeatureVector normalized = parameters.Normalize(
            Raw(0.5, 0.55, 0.5, 120.0, 0.4, 0.1, 0.15, 0.05, -10.0));

        double speechiness = normalized[SimilarityFeature.Speechiness];
        Assert.False(double.IsNaN(speechiness) || double.IsInfinity(speechiness));
        Assert.Equal(0.0, speechiness, Tolerance);
    }

    [Fact]
    public void Normalize_SameParametersForSeedAndCandidate_ProducesConsistentTransform()
    {
        // Consistência semente/candidato: a MESMA instância de parâmetros aplicada ao mesmo vetor cru dá o mesmo
        // vetor normalizado. É o que fecha o skew — não há duas transformações que possam divergir.
        FeatureNormalizationParameters parameters = FeatureNormalizationParameters.LearnFrom(LearningCatalog());
        SimilarityFeatureVector raw = Raw(0.5, 0.55, 0.5, 120.0, 0.4, 0.1, 0.15, 0.05, -10.0);

        SimilarityFeatureVector asSeed = parameters.Normalize(raw);
        SimilarityFeatureVector asCandidate = parameters.Normalize(raw);

        Assert.Equal(asSeed, asCandidate);
    }

    [Fact]
    public void Normalize_ManuallyMatchesZScoreFormula()
    {
        FeatureNormalizationParameters parameters = FeatureNormalizationParameters.LearnFrom(LearningCatalog());
        SimilarityFeatureVector raw = Raw(0.5, 0.55, 0.5, 130.0, 0.4, 0.1, 0.15, 0.05, -10.0);

        SimilarityFeatureVector normalized = parameters.Normalize(raw);

        int tempo = (int)SimilarityFeature.Tempo;
        double expectedTempo = (130.0 - parameters.Means[tempo]) / parameters.StandardDeviations[tempo];
        Assert.Equal(expectedTempo, normalized[SimilarityFeature.Tempo], Tolerance);
    }

    /// <summary>
    /// O TESTE central do card. Duas faixas candidatas contra uma semente:
    ///  - "sonicTwin": idêntica à semente nas 0–1, distante SÓ em Tempo/Loudness;
    ///  - "scaleTwin": idêntica em Tempo/Loudness, distante nas 0–1.
    /// SEM normalização, o cosine cru é dominado por Tempo/Loudness (magnitude enorme), então "scaleTwin" (que
    /// casa Tempo/Loudness) parece MAIS perto — resultado plausível e ERRADO. COM normalização, o vizinho certo
    /// ("sonicTwin", perto no perfil sonoro real) passa a vencer.
    /// </summary>
    [Fact]
    public void Normalization_FlipsTheDominantFeatureFromScaleToActualProfile()
    {
        SimilarityFeatureVector seed =
            Raw(0.80, 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0);

        // Mesmo perfil 0–1 da semente; só Tempo/Loudness bem diferentes.
        SimilarityFeatureVector sonicTwin =
            Raw(0.80, 0.80, 0.80, 60.0, 0.10, 0.10, 0.10, 0.05, -30.0);

        // Mesmíssimos Tempo/Loudness da semente; perfil 0–1 oposto.
        SimilarityFeatureVector scaleTwin =
            Raw(0.10, 0.10, 0.10, 120.0, 0.90, 0.80, 0.70, 0.60, -8.0);

        // --- SEM normalização: Tempo/Loudness dominam, scaleTwin vence indevidamente ---
        double rawToSonic = CosineSimilarity.Between(seed, sonicTwin);
        double rawToScale = CosineSimilarity.Between(seed, scaleTwin);
        Assert.True(
            rawToScale > rawToSonic,
            $"Sem normalização, Tempo/Loudness deveriam dominar (scaleTwin>sonicTwin). " +
            $"Obtido: scale={rawToScale:F4}, sonic={rawToSonic:F4}.");

        // --- COM normalização (μ/σ aprendidos com a heterogeneidade real): o perfil sonoro passa a mandar ---
        var catalog = new List<SimilarityFeatureVector>(LearningCatalog()) { seed, sonicTwin, scaleTwin };
        FeatureNormalizationParameters parameters = FeatureNormalizationParameters.LearnFrom(catalog);

        double normToSonic = CosineSimilarity.Between(
            parameters.Normalize(seed), parameters.Normalize(sonicTwin));
        double normToScale = CosineSimilarity.Between(
            parameters.Normalize(seed), parameters.Normalize(scaleTwin));

        Assert.True(
            normToSonic > normToScale,
            $"Com normalização, nenhuma feature domina por escala e o vizinho de perfil (sonicTwin) deveria " +
            $"vencer. Obtido: sonic={normToSonic:F4}, scale={normToScale:F4}.");
    }
}
