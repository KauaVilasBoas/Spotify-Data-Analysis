namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>
/// Política de tratamento das faixas cujas audio-features foram <b>imputadas</b> (preenchidas pela mediana
/// estratificada por gênero, no E1.5) em vez de medidas.
///
/// <para>A imputação por mediana comprime a variância e injeta um artefato correlacionado com o gênero: o
/// modelo pode aprender "essa feature vale exatamente a mediana do gênero X" e usar isso como proxy do
/// gênero, acertando pelo motivo errado. Por isso a escolha é explícita e nomeada, nunca implícita.</para>
/// </summary>
public enum ImputedFeaturePolicy
{
    /// <summary>Treina apenas com features medidas. Padrão do projeto.</summary>
    ExcludeImputed = 0,

    /// <summary>
    /// Admite faixas imputadas no conjunto. Existe para a leitura de robustez — comparar as métricas do
    /// modelo nos dois recortes — e não para virar o conjunto de treino padrão.
    /// </summary>
    IncludeImputed = 1
}
