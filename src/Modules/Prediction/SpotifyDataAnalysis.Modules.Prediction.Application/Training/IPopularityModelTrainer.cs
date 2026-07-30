using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Training;

/// <summary>
/// Porta de treino do modelo de popularidade. A Application pede um treino e recebe de volta um relatório de
/// métricas em tipos próprios; o pipeline do ML.NET vive no adaptador, dentro da Infrastructure.
///
/// <para>O artefato treinado NÃO atravessa esta fronteira nesta fatia: persistir e versionar o modelo é o
/// E3.4. Aqui o que interessa é a resposta medida para "dá para prever popularidade a partir do áudio?".</para>
/// </summary>
public interface IPopularityModelTrainer
{
    /// <summary>Treina e avalia o modelo com as opções de dataset informadas.</summary>
    Task<ModelTrainingReport> TrainAsync(
        TrainingDatasetSplitOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Métricas de um conjunto avaliado, com o baseline ao lado. Modelo sem baseline é propaganda: as duas
/// leituras andam sempre juntas para que o número do modelo possa ser julgado.
/// </summary>
/// <param name="Label">Qual conjunto foi avaliado (só medidas, ou incluindo imputadas).</param>
/// <param name="TestSampleCount">Tamanho do conjunto de teste avaliado.</param>
/// <param name="Model">Métricas do modelo treinado.</param>
/// <param name="MeanBaseline">Métricas de prever sempre a média do treino — o piso a ser batido.</param>
/// <param name="LinearBaseline">
/// Métricas de uma regressão linear sobre as mesmas features — o contraponto entre "média burra" e árvore.
/// </param>
/// <param name="Gate">Veredito do gate de qualidade do modelo contra o baseline da média.</param>
public sealed record ModelEvaluationReport(
    string Label,
    long TestSampleCount,
    RegressionMetrics Model,
    RegressionMetrics MeanBaseline,
    RegressionMetrics LinearBaseline,
    ModelQualityVerdict Gate);

/// <summary>
/// Estabilidade das métricas entre folds da validação cruzada. O desvio importa tanto quanto a média: desvio
/// grande significa que o número único do holdout é sorte, não medida.
/// </summary>
/// <param name="Folds">Número de folds executados.</param>
/// <param name="MeanRSquared">R² médio entre folds.</param>
/// <param name="StandardDeviationRSquared">Desvio-padrão do R² entre folds.</param>
/// <param name="MeanMeanAbsoluteError">MAE médio entre folds.</param>
/// <param name="StandardDeviationMeanAbsoluteError">Desvio-padrão do MAE entre folds.</param>
public sealed record CrossValidationReport(
    int Folds,
    double MeanRSquared,
    double StandardDeviationRSquared,
    double MeanMeanAbsoluteError,
    double StandardDeviationMeanAbsoluteError);

/// <summary>
/// O relatório completo de um treino: o que foi treinado, com o quê, e o que saiu medido.
/// </summary>
/// <param name="Trainer">Nome do algoritmo campeão.</param>
/// <param name="Features">Features que compuseram o vetor, na ordem em que entraram.</param>
/// <param name="Seed">Semente usada no split e no <c>MLContext</c> — a chave da reprodutibilidade.</param>
/// <param name="TestFraction">Fração reservada para teste.</param>
/// <param name="TrainedOnImputed">Se faixas com features imputadas entraram no TREINO.</param>
/// <param name="TrainingSampleCount">Tamanho do conjunto de treino.</param>
/// <param name="Primary">Avaliação no conjunto em que o modelo foi treinado.</param>
/// <param name="ImputedComparison">
/// Avaliação do MESMO modelo no conjunto ampliado com imputadas. Presente só quando o treino as excluiu — é a
/// leitura que mostra o efeito da imputação em vez de escondê-lo.
/// </param>
/// <param name="CrossValidation">Estabilidade entre folds, sobre o conjunto de treino.</param>
/// <param name="ElapsedMilliseconds">Custo de parede do treino completo.</param>
/// <param name="Publication">O que aconteceu com a versão treinada: registrada, e promovida ou não.</param>
public sealed record ModelTrainingReport(
    string Trainer,
    IReadOnlyList<string> Features,
    int Seed,
    double TestFraction,
    bool TrainedOnImputed,
    long TrainingSampleCount,
    ModelEvaluationReport Primary,
    ModelEvaluationReport? ImputedComparison,
    CrossValidationReport CrossValidation,
    long ElapsedMilliseconds,
    ModelPublicationReport Publication);

/// <summary>
/// O destino da versão treinada (E3.4). Toda versão é registrada — inclusive a reprovada, porque perder o
/// registro de um treino ruim é perder a evidência de que ele aconteceu. Só a promoção é condicional.
/// </summary>
/// <param name="Version">Número sequencial da versão gravada.</param>
/// <param name="Promoted">Se a versão virou a corrente.</param>
/// <param name="Reason">Por que foi ou não promovida — o resultado explica a si mesmo.</param>
/// <param name="ArtifactHash">Hash do artefato gravado.</param>
/// <param name="ArtifactSizeBytes">Tamanho do <c>.zip</c> serializado.</param>
public sealed record ModelPublicationReport(
    int Version,
    bool Promoted,
    string Reason,
    string ArtifactHash,
    long ArtifactSizeBytes);
