using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.DependencyInjection;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Recommendations;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Infrastructure;

/// <summary>
/// Garante que a flag <c>Prediction:Recommendations:WarmUpEnabled</c> controla o registro do
/// <see cref="SimilarityIndexWarmUpService"/> — convenção estabelecida em
/// <c>Jobs:PlaylistIngestion:Enabled</c> e <c>Jobs:CatalogEnrichment:Enabled</c>.
///
/// <para>Com a flag desligada, nenhum <see cref="IHostedService"/> do warm-up é registrado;
/// portanto, ambientes de teste que sobem o Program real sem banco disponível não disparam
/// a varredura do catálogo — que falharia em silêncio.</para>
/// </summary>
public sealed class WarmUpRegistrationTests
{
    private static IConfiguration BuildConfig(string warmUpEnabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("ConnectionStrings:SpotifyDb",
                    "Host=localhost;Port=5432;Database=test;Username=test;Password=test"),
                new("Prediction:Recommendations:WarmUpEnabled", warmUpEnabled),
                new("Prediction:TrainingDataset:Seed", "42"),
                new("Prediction:TrainingDataset:TestFraction", "0.2"),
                new("Prediction:TrainingDataset:ReadBatchSize", "1000"),
            ])
            .Build();

    [Fact]
    public void AddPredictionModule_WhenWarmUpDisabled_DoesNotRegisterWarmUpHostedService()
    {
        // Arrange
        IConfiguration config = BuildConfig("false");
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddPredictionModule(config);

        // Assert: nenhum IHostedService implementado por SimilarityIndexWarmUpService.
        bool warmUpRegistered = services.Any(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(SimilarityIndexWarmUpService));

        Assert.False(warmUpRegistered,
            "SimilarityIndexWarmUpService não deve ser registrado quando WarmUpEnabled=false.");
    }

    [Fact]
    public void AddPredictionModule_WhenWarmUpEnabled_RegistersWarmUpHostedService()
    {
        // Arrange
        IConfiguration config = BuildConfig("true");
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddPredictionModule(config);

        // Assert: o IHostedService está no container.
        bool warmUpRegistered = services.Any(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(SimilarityIndexWarmUpService));

        Assert.True(warmUpRegistered,
            "SimilarityIndexWarmUpService deve ser registrado quando WarmUpEnabled=true.");
    }

    [Fact]
    public void AddPredictionModule_WhenWarmUpFlagAbsent_RegistersWarmUpHostedService()
    {
        // Default ausente → true: produção não pode depender de alguém lembrar de ligar.
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("ConnectionStrings:SpotifyDb",
                    "Host=localhost;Port=5432;Database=test;Username=test;Password=test"),
                new("Prediction:TrainingDataset:Seed", "42"),
                new("Prediction:TrainingDataset:TestFraction", "0.2"),
                new("Prediction:TrainingDataset:ReadBatchSize", "1000"),
            ])
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPredictionModule(config);

        bool warmUpRegistered = services.Any(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(SimilarityIndexWarmUpService));

        Assert.True(warmUpRegistered,
            "SimilarityIndexWarmUpService deve ser registrado quando a flag está ausente (default=true).");
    }
}
