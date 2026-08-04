using SpotifyDataAnalysis.Modules.Prediction.Domain.Models;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Domain;

/// <summary>
/// O agregado de versão do modelo: o que ele recusa registrar e o que ele garante sobre publicação.
/// </summary>
public sealed class ModelVersionTests
{
    private static readonly string[] Features = ["Danceability", "Energy"];

    private static ModelVersion Register(
        IEnumerable<string>? features = null,
        byte[]? artifact = null,
        IEnumerable<FeatureImportance>? featureImportance = null) =>
        ModelVersion.Register(
            DateTime.UtcNow,
            "FastTree",
            features ?? Features,
            featureImportance,
            seed: 42,
            testFraction: 0.2,
            trainingSampleCount: 800,
            testSampleCount: 200,
            trainedOnImputed: false,
            new RegressionMetrics(0.15, 15.1, 18.9),
            new RegressionMetrics(0, 17.2, 20.5),
            artifact ?? [1, 2, 3],
            "abc123");

    [Fact]
    public void Register_StartsAsCandidate_NotCurrent()
    {
        // Registrar não é publicar: promover é ato separado, sujeito à política.
        ModelVersion version = Register();

        Assert.Equal(ModelVersionStatus.Candidate, version.Status);
        Assert.False(version.IsCurrent);
    }

    [Fact]
    public void Register_WithoutFeatureSet_Fails()
    {
        // Um .zip sem feature set não é auditável nem validável na carga.
        Assert.Throws<DomainException>(() => Register(features: []));
    }

    [Fact]
    public void Register_WithoutArtifact_Fails()
    {
        Assert.Throws<DomainException>(() => Register(artifact: []));
    }

    [Fact]
    public void Register_KeepsTheBaselineMetrics_SoTheVersionIsComparable()
    {
        ModelVersion version = Register();

        Assert.Equal(17.2, version.BaselineMetrics.MeanAbsoluteError);
        Assert.Equal(15.1, version.ModelMetrics.MeanAbsoluteError);
    }

    [Fact]
    public void Promote_MakesItCurrent_AndDemoteReverts()
    {
        ModelVersion version = Register();

        version.Promote();
        Assert.True(version.IsCurrent);

        version.Demote();
        Assert.False(version.IsCurrent);
    }

    [Fact]
    public void Promote_IsIdempotent()
    {
        ModelVersion version = Register();

        version.Promote();
        version.Promote();

        Assert.Equal(ModelVersionStatus.Current, version.Status);
    }

    [Fact]
    public void IsCompatibleWith_RequiresTheSameFeaturesInTheSameOrder()
    {
        // Ordem importa: o vetor de features é posicional, então a mesma lista embaralhada produz predição
        // errada em silêncio — que é pior que falhar.
        ModelVersion version = Register();

        Assert.True(version.IsCompatibleWith(["Danceability", "Energy"]));
        Assert.False(version.IsCompatibleWith(["Energy", "Danceability"]));
        Assert.False(version.IsCompatibleWith(["Danceability"]));
        Assert.False(version.IsCompatibleWith(["Danceability", "Energy", "Valence"]));
    }
}
