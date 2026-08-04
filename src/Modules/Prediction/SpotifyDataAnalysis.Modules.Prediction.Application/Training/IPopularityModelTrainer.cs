using SpotifyDataAnalysis.Modules.Prediction.Domain.Models;
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
/// Uma linha da tabela comparativa do E3.3: as métricas de um feature set candidato, avaliado no MESMO conjunto
/// de teste que todos os outros. É o registro que sustenta a eleição do campeão por medição — cada bloco só
/// permanece se pagar o próprio custo.
/// </summary>
/// <param name="Label">Rótulo do conjunto na tabela (<c>baseline</c>, <c>+A</c>, <c>+B</c>, <c>A+B</c>).</param>
/// <param name="Features">Nomes lógicos das features do conjunto, na ordem em que entraram.</param>
/// <param name="Metrics">R²/MAE/RMSE do modelo treinado com este feature set, no conjunto de teste.</param>
public sealed record FeatureSetEvaluationRow(
    string Label,
    IReadOnlyList<string> Features,
    RegressionMetrics Metrics);

/// <summary>
/// A medição comparativa incremental do E3.3 e a decisão que ela sustenta: qual feature set foi eleito campeão
/// e por quê. O baseline (E3.2) é a âncora fixa; cada bloco é medido isolado sobre ele e a combinação fecha a
/// tabela. Um bloco só entra no campeão se seu ganho isolado de MAE bater <see cref="RequiredBlockMaeGain"/>.
/// </summary>
/// <param name="Baseline">O feature set do E3.2 — a linha de base contra a qual tudo é comparado.</param>
/// <param name="Candidates">Os demais conjuntos avaliados (+A, +B, A+B), com suas métricas.</param>
/// <param name="ChampionLabel">Rótulo do feature set eleito campeão.</param>
/// <param name="ChampionRationale">Justificativa da eleição, bloco a bloco — inclui os que não pagaram.</param>
/// <param name="RequiredBlockMaeGain">Ganho mínimo de MAE (fração) para um bloco permanecer no campeão.</param>
public sealed record FeatureSetComparisonReport(
    FeatureSetEvaluationRow Baseline,
    IReadOnlyList<FeatureSetEvaluationRow> Candidates,
    string ChampionLabel,
    string ChampionRationale,
    double RequiredBlockMaeGain);

/// <summary>
/// O ranking de importância de features do CAMPEÃO (E3.6), medido por permutação sobre o conjunto de teste, e
/// o custo dessa medição.
///
/// <para>O <see cref="ElapsedMilliseconds"/> não é enfeite de log: a permutação reexecuta a avaliação uma vez
/// por slot do vetor, então é ele que responde se a medição cabe em todo treino ou só na promoção. Sem o
/// número, a decisão vira palpite.</para>
/// </summary>
/// <param name="PermutationCount">Quantas permutações por slot sustentam a média e a dispersão publicadas.</param>
/// <param name="SlotCount">Quantas colunas do vetor foram permutadas — o custo real da medição.</param>
/// <param name="Features">O ranking agregado por feature lógica, da mais para a menos importante.</param>
/// <param name="ElapsedMilliseconds">Tempo que a medição acrescentou ao treino.</param>
public sealed record FeatureImportanceReport(
    int PermutationCount,
    int SlotCount,
    IReadOnlyList<FeatureImportance> Features,
    long ElapsedMilliseconds);

/// <summary>
/// O relatório completo de um treino: o que foi treinado, com o quê, e o que saiu medido.
/// </summary>
/// <param name="Trainer">Nome do algoritmo campeão.</param>
/// <param name="Features">Features que compuseram o vetor do CAMPEÃO, na ordem em que entraram.</param>
/// <param name="Seed">Semente usada no split e no <c>MLContext</c> — a chave da reprodutibilidade.</param>
/// <param name="TestFraction">Fração reservada para teste.</param>
/// <param name="TrainedOnImputed">Se faixas com features imputadas entraram no TREINO.</param>
/// <param name="TrainingSampleCount">Tamanho do conjunto de treino.</param>
/// <param name="Primary">Avaliação do CAMPEÃO no conjunto em que o modelo foi treinado.</param>
/// <param name="ImputedComparison">
/// Avaliação do MESMO modelo campeão no conjunto ampliado com imputadas. Presente só quando o treino as excluiu
/// — é a leitura que mostra o efeito da imputação em vez de escondê-lo.
/// </param>
/// <param name="CrossValidation">Estabilidade entre folds do campeão, sobre o conjunto de treino.</param>
/// <param name="FeatureSetComparison">A tabela comparativa do E3.3 e a eleição do campeão por medição.</param>
/// <param name="FeatureImportance">
/// O ranking de importância do campeão (E3.6), medido por permutação sobre o conjunto de teste e gravado junto
/// da versão — o <c>GET /api/model/current</c> lê daí, nunca recalcula.
/// </param>
/// <param name="ElapsedMilliseconds">Custo de parede do treino completo, JÁ INCLUINDO a medição de importância.</param>
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
    FeatureSetComparisonReport FeatureSetComparison,
    FeatureImportanceReport FeatureImportance,
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
