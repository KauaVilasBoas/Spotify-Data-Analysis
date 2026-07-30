using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using SpotifyDataAnalysis.Infrastructure.DependencyInjection;
using SpotifyDataAnalysis.Modules.Prediction.Application;
using SpotifyDataAnalysis.Modules.Prediction.Application.Training;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.DependencyInjection;

/// <summary>
/// Composição de DI do módulo Prediction. Chamado por <see cref="PredictionModule"/> durante o bootstrap.
///
/// <para>O módulo ainda não tem write-side: a persistência de versões do modelo chega no E3.4 (binário em
/// <c>bytea</c> no Postgres), e é só então que entram DbContext, UnitOfWork e TransactionBehavior. Por
/// enquanto, o acesso a dados é leitura via Dapper — o <c>DbConnectionFactory</c> usado por
/// <c>BaseDataAccess</c> já vem da Infrastructure compartilhada.</para>
/// </summary>
public static class PredictionModuleServiceExtensions
{
    /// <summary>
    /// Registra a configuração do dataset, o <see cref="MLContext"/> semeado, os adaptadores de leitura e
    /// montagem e os handlers CQRS da Application (por varredura).
    ///
    /// <para>O <see cref="MLContext"/> é singleton: é caro de criar e reutilizável entre operações
    /// independentes. Sua semente vem da MESMA configuração do split — duas fontes de aleatoriedade com
    /// sementes diferentes tornariam o treino irreprodutível justamente pela metade que ninguém olha.</para>
    /// </summary>
    public static IServiceCollection AddPredictionModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TrainingDatasetSettings>(
            configuration.GetSection(TrainingDatasetSettings.SectionName));

        services.AddSingleton(serviceProvider => new MLContext(
            seed: serviceProvider.GetRequiredService<IOptions<TrainingDatasetSettings>>().Value.Seed));

        services.AddScoped<ITrackTrainingCandidateSource, CatalogTrackTrainingCandidateSource>();
        services.AddScoped<ITrainingDatasetProvider, MlNetTrainingDatasetProvider>();

        services.AddHandlersFromAssembly(typeof(PredictionApplicationAssemblyReference).Assembly);

        return services;
    }
}
