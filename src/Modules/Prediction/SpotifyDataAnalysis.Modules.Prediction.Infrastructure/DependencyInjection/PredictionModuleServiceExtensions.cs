using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using SpotifyDataAnalysis.Infrastructure.DependencyInjection;
using SpotifyDataAnalysis.Modules.Prediction.Application;
using SpotifyDataAnalysis.Modules.Prediction.Application.Inference;
using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Application.Training;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Models;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Inference;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Persistence;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Recommendations;
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

        // O provider é registrado pelo tipo CONCRETO e a porta aponta para a MESMA instância: o treino (E3.2)
        // precisa das IDataViews que só o concreto expõe, enquanto a Application enxerga apenas a porta.
        services.AddScoped<MlNetTrainingDatasetProvider>();
        services.AddScoped<ITrainingDatasetProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<MlNetTrainingDatasetProvider>());

        services.AddScoped<IPopularityModelTrainer, FastTreePopularityModelTrainer>();

        // --- Write-side (E3.4): versões do modelo no schema "prediction" ---
        string? connectionString = configuration.GetConnectionString("SpotifyDb");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Connection string 'SpotifyDb' não configurada. Defina em User Secrets ou na variável de " +
                "ambiente ConnectionStrings__SpotifyDb.");

        services.AddDbContext<PredictionDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", schema: "prediction");
                npgsql.MigrationsAssembly(typeof(PredictionDbContext).Assembly.GetName().Name);
            }));

        services.AddScoped<IModelVersionRepository, ModelVersionRepository>();

        // Cache do modelo corrente é SINGLETON: desserializar o .zip a cada request inviabilizaria a
        // inferência do E3.5. A invalidação na promoção é o que dispensa reiniciar a aplicação.
        services.AddSingleton<CurrentModelCache>();

        // --- Inferência (E3.5): serve o modelo corrente ---

        // Leitura pontual das features de uma faixa do schema "catalog" (modo trackId). Mesma fronteira do E3.1.
        services.AddScoped<ITrackFeatureSource, CatalogTrackFeatureSource>();

        // Pool próprio de PredictionEngine (decisão A do fork): SINGLETON, porque só faz sentido reaproveitar
        // engines entre requisições. Consome o ITransformer cacheado, invalidado por versão na promoção.
        services.AddSingleton<PopularityPredictionEnginePool>();

        // O predictor é SCOPED: depende do repositório EF (per-request) para resolver a versão corrente. O
        // estado durável (modelo desserializado, engines) vive no cache e no pool, ambos singletons.
        services.AddScoped<IPopularityPredictor, MlNetPopularityPredictor>();

        // Validador FluentValidation do endpoint de predição — consumido pelo ValidationBehavior do mediator
        // para que um request malformado vire 400 (ProblemDetails) em vez de 500 ou de uma predição sem sentido.
        // Registro explícito (e não AddValidatorsFromAssembly) para não arrastar o pacote
        // FluentValidation.DependencyInjectionExtensions por causa de um único validador.
        services.AddScoped<IValidator<PredictPopularityCommand>, PredictPopularityCommandValidator>();

        // --- Recomendação content-based (E4.1): motor de similaridade sobre audio-features ---

        // Leitura das faixas elegíveis do schema "catalog" para montar o índice. Mesma fronteira do E3.1
        // (Dapper via BaseDataAccess), scoped porque depende do DbConnectionFactory per-request.
        services.AddScoped<ISimilarityFeatureSource, CatalogSimilarityFeatureSource>();

        // Índice de similaridade em cache: SINGLETON, porque montar o espaço (varrer ~90k faixas e aprender μ/σ)
        // é caro e o resultado é reusável entre requisições — o análogo do cache do modelo corrente do E3.5. Lê o
        // source scoped abrindo um scope próprio via IServiceScopeFactory.
        services.AddSingleton<ITrackSimilarityIndexProvider, CachedTrackSimilarityIndexProvider>();

        services.AddHandlersFromAssembly(typeof(PredictionApplicationAssemblyReference).Assembly);

        return services;
    }
}
