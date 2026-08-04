using Microsoft.ML;
using SpotifyDataAnalysis.Modules.Prediction.Application.Training;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Models;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Training;

/// <summary>
/// A medição de importância por permutação do E3.6, sobre a MESMA fixture determinística do gate do E3.2.
///
/// <para>A fixture tem sinal conhecido por construção (<c>popularity ≈ 20 + 60·danceability + 20·energy</c>),
/// então o ranking tem uma resposta certa: <c>Danceability</c> tem de liderar. Um ranking de importância que
/// não recupera um sinal que nós mesmos plantamos não descreve modelo nenhum.</para>
/// </summary>
public sealed class FeatureImportanceTests
{
    private const int Seed = 20260730;

    private static (PopularityModelPipeline Pipeline, IDataView Training, IDataView Test, MLContext MlContext)
        Arrange(IReadOnlyList<TrackTrainingSample> samples)
    {
        var mlContext = new MLContext(seed: Seed);
        var pipeline = new PopularityModelPipeline(mlContext);

        int split = (int)(samples.Count * 0.8);

        IDataView training = mlContext.Data.LoadFromEnumerable(
            samples.Take(split).Select(PopularityTrainingRow.FromSample));
        IDataView test = mlContext.Data.LoadFromEnumerable(
            samples.Skip(split).Select(PopularityTrainingRow.FromSample));

        return (pipeline, training, test, mlContext);
    }

    private static FeatureImportanceReport Measure(
        IReadOnlyList<TrackTrainingSample> samples, PopularityFeatureSet featureSet)
    {
        (PopularityModelPipeline pipeline, IDataView training, IDataView test, MLContext mlContext) =
            Arrange(samples);

        ITransformer model = pipeline.Train(training, featureSet);

        return new PermutationFeatureImportanceCalculator(mlContext).Measure(model, test);
    }

    [Fact]
    public void FeatureImportance_PutsTheKnownInformativeFeatureAtTheTop()
    {
        FeatureImportanceReport report = Measure(LearnableFixture.Create(), PopularityFeatureSet.Baseline);

        FeatureImportance top = report.Features[0];

        Assert.Equal(nameof(PopularityTrainingRow.Danceability), top.Feature);
        Assert.True(
            top.RSquaredDrop.Mean > 0,
            $"Embaralhar a feature dominante tem de PIORAR o modelo, e a queda medida foi " +
            $"{top.RSquaredDrop.Mean:F6} de R².");
    }

    [Fact]
    public void FeatureImportance_RanksTheSecondaryFeatureAboveThePureNoiseOnes()
    {
        FeatureImportanceReport report = Measure(LearnableFixture.Create(), PopularityFeatureSet.Baseline);

        Assert.Equal(nameof(PopularityTrainingRow.Energy), report.Features[1].Feature);

        double noiseCeiling = report.Features
            .Where(feature => feature.Feature is not (nameof(PopularityTrainingRow.Danceability)
                or nameof(PopularityTrainingRow.Energy)))
            .Max(feature => feature.RSquaredDrop.Mean);

        Assert.True(
            report.Features[1].RSquaredDrop.Mean > noiseCeiling,
            "A segunda feature plantada tem de superar TODAS as que não carregam sinal.");
    }

    /// <summary>
    /// Nunca um número solto: a permutação é estocástica, e sem a dispersão o leitor não tem como saber se a
    /// diferença entre duas linhas do ranking significa alguma coisa.
    /// </summary>
    [Fact]
    public void FeatureImportance_ReportsDispersionAlongsideTheMean()
    {
        FeatureImportanceReport report = Measure(LearnableFixture.Create(), PopularityFeatureSet.Baseline);

        Assert.Equal(PermutationFeatureImportanceCalculator.PermutationCount, report.PermutationCount);
        Assert.All(report.Features, feature =>
        {
            Assert.True(feature.RSquaredDrop.StandardDeviation >= 0);
            Assert.True(feature.MeanAbsoluteErrorIncrease.StandardDeviation >= 0);
        });
        Assert.Contains(report.Features, feature => feature.RSquaredDrop.StandardDeviation > 0);
    }

    /// <summary>
    /// R² cai e MAE sobe quando a feature informa: as duas leituras têm de apontar para o mesmo lado, senão o
    /// ranking ordenado por uma contradiz a outra.
    /// </summary>
    [Fact]
    public void FeatureImportance_OrientsBothMetricsSoThatHigherAlwaysMeansMoreImportant()
    {
        FeatureImportanceReport report = Measure(LearnableFixture.Create(), PopularityFeatureSet.Baseline);

        FeatureImportance top = report.Features[0];

        Assert.True(
            top.MeanAbsoluteErrorIncrease.Mean > 0,
            $"Embaralhar a feature dominante tem de AUMENTAR o erro, e o aumento medido foi " +
            $"{top.MeanAbsoluteErrorIncrease.Mean:F6} ponto de popularidade.");
    }

    /// <summary>
    /// Sem a agregação, o gênero apareceria como uma coluna por categoria — 113 linhas anônimas no catálogo
    /// real. O bloco tem de sair como UMA linha, e ela precisa saber quantos slots resumiu.
    /// </summary>
    [Fact]
    public void FeatureImportance_CollapsesOneHotSlotsUnderTheBlockName()
    {
        FeatureImportanceReport report = Measure(
            LearnableFixture.CreateWithGenreSignal(), PopularityFeatureSet.Genre);

        FeatureImportance genre = Assert.Single(
            report.Features, feature => feature.Feature == nameof(PopularityTrainingRow.Genre));

        Assert.True(genre.SlotCount > 1, "O bloco de gênero da fixture tem mais de uma categoria.");
        Assert.DoesNotContain(
            report.Features,
            feature => feature.Feature.Contains('.', StringComparison.Ordinal));
        Assert.All(
            report.Features,
            feature => Assert.Contains(
                feature.Feature,
                PopularityFeatureSetDescriptor.LogicalFeatureNames(PopularityFeatureSet.Genre)));
    }

    /// <summary>
    /// Contraprova da agregação: se os slots fossem somados errado, o bloco que CARREGA o sinal não
    /// lideraria. Nesta fixture a popularidade é quase inteiramente função do gênero.
    /// </summary>
    [Fact]
    public void FeatureImportance_PutsTheCategoricalBlockAtTheTop_WhenTheSignalLivesInIt()
    {
        FeatureImportanceReport report = Measure(
            LearnableFixture.CreateWithGenreSignal(), PopularityFeatureSet.Genre);

        Assert.Equal(nameof(PopularityTrainingRow.Genre), report.Features[0].Feature);
    }

    [Fact]
    public void FeatureImportance_IsMeasuredOnceAndCarriesItsOwnCost()
    {
        FeatureImportanceReport report = Measure(LearnableFixture.Create(), PopularityFeatureSet.Baseline);

        Assert.Equal(PopularityModelPipeline.BaselineFeatureColumns.Length, report.SlotCount);
        Assert.True(report.ElapsedMilliseconds >= 0);
    }
}
