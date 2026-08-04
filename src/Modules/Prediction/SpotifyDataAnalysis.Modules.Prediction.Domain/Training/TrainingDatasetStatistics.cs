namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>
/// O censo do dataset de treino: quantas faixas o catálogo tem, quantas dá para treinar e quantas ficaram de
/// fora <b>por cada motivo</b>. É a resposta em números auditáveis para "com quantas faixas dá para treinar?",
/// que precede qualquer treino.
///
/// <para>As contagens fecham por construção:
/// <c>TotalTracks = ExcludedMissingPopularity + ExcludedMissingAudioFeatures + ExcludedIncompleteAudioFeatures
/// + StructurallyEligible</c>, e <c>StructurallyEligible = EligibleWithMeasuredFeatures +
/// EligibleWithImputedFeatures</c>. O conjunto que vai a treino/teste é o recorte da política de imputação em
/// vigor — por padrão, só o medido.</para>
/// </summary>
/// <param name="TotalTracks">Total de faixas no catálogo.</param>
/// <param name="ExcludedMissingPopularity">Faixas sem popularidade (sem alvo).</param>
/// <param name="ExcludedMissingAudioFeatures">Faixas sem audio-features (jsonb nulo).</param>
/// <param name="ExcludedIncompleteAudioFeatures">Faixas com audio-features, mas faltando alguma grandeza numérica.</param>
/// <param name="EligibleWithMeasuredFeatures">Faixas estruturalmente elegíveis cujas features foram MEDIDAS.</param>
/// <param name="EligibleWithImputedFeatures">Faixas estruturalmente elegíveis cujas features foram IMPUTADAS.</param>
/// <param name="ExcludedByImputationPolicy">Elegíveis descartadas pela política de imputação em vigor.</param>
/// <param name="TrainingSampleCount">Tamanho do conjunto de treino após o split.</param>
/// <param name="TestSampleCount">Tamanho do conjunto de teste após o split.</param>
/// <param name="AppliedImputedFeaturePolicy">A política de imputação efetivamente aplicada.</param>
/// <param name="Seed">A semente do split aplicada.</param>
/// <param name="TestFraction">A fração de teste pedida ao split.</param>
public sealed record TrainingDatasetStatistics(
    long TotalTracks,
    long ExcludedMissingPopularity,
    long ExcludedMissingAudioFeatures,
    long ExcludedIncompleteAudioFeatures,
    long EligibleWithMeasuredFeatures,
    long EligibleWithImputedFeatures,
    long ExcludedByImputationPolicy,
    long TrainingSampleCount,
    long TestSampleCount,
    ImputedFeaturePolicy AppliedImputedFeaturePolicy,
    int Seed,
    double TestFraction)
{
    /// <summary>
    /// Faixas que passam nos critérios estruturais, INCLUSIVE as imputadas. É o "conjunto que inclui
    /// imputadas" pedido pela DP-2 para a comparação de robustez ficar visível ao lado do conjunto medido.
    /// </summary>
    public long StructurallyEligible => EligibleWithMeasuredFeatures + EligibleWithImputedFeatures;

    /// <summary>Faixas efetivamente usadas para montar treino + teste, depois da política de imputação.</summary>
    public long TrainableTracks => TrainingSampleCount + TestSampleCount;

    /// <summary>Soma de todas as exclusões, por qualquer motivo.</summary>
    public long ExcludedTracks =>
        ExcludedMissingPopularity
        + ExcludedMissingAudioFeatures
        + ExcludedIncompleteAudioFeatures
        + ExcludedByImputationPolicy;
}
