using Microsoft.Extensions.Options;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Training;

/// <summary>
/// Treina o modelo de popularidade e devolve as métricas do run (E3.2). É command, e não query, porque o
/// treino é caro e tem efeito — mesmo que a persistência do artefato só chegue no E3.4.
/// </summary>
public sealed record TrainPopularityModelCommand : ICommand<ModelTrainingReport>
{
    private readonly double? _testFraction;

    /// <summary>Semente do split e do <c>MLContext</c>. Ausente usa a de configuração.</summary>
    public int? Seed { get; init; }

    /// <summary>Fração de teste, presa ao intervalo aceito pelo domínio. Ausente usa a de configuração.</summary>
    public double? TestFraction
    {
        get => _testFraction;
        init => _testFraction = value is null
            ? null
            : Math.Clamp(
                value.Value,
                TrainingDatasetSplitOptions.MinimumTestFraction,
                TrainingDatasetSplitOptions.MaximumTestFraction);
    }

    /// <summary>
    /// Se faixas com features IMPUTADAS entram no TREINO. O padrão é <c>false</c>: a imputação pela mediana
    /// estratificada por gênero comprime a variância e ensina o modelo a reconhecer a mediana do gênero em vez
    /// da música. Com o padrão, o relatório traz também a avaliação no conjunto ampliado, para o efeito da
    /// imputação aparecer medido.
    /// </summary>
    public bool IncludeImputed { get; init; }
}

internal sealed class TrainPopularityModelCommandHandler
    : ICommandHandler<TrainPopularityModelCommand, ModelTrainingReport>
{
    private readonly IPopularityModelTrainer _trainer;
    private readonly TrainingDatasetSettings _settings;

    public TrainPopularityModelCommandHandler(
        IPopularityModelTrainer trainer,
        IOptions<TrainingDatasetSettings> settings)
    {
        _trainer = trainer;
        _settings = settings.Value;
    }

    public Task<ModelTrainingReport> HandleAsync(
        TrainPopularityModelCommand request, CancellationToken cancellationToken = default)
    {
        TrainingDatasetSplitOptions options = TrainingDatasetSplitOptions.Create(
            request.Seed ?? _settings.Seed,
            request.TestFraction ?? _settings.TestFraction,
            request.IncludeImputed
                ? ImputedFeaturePolicy.IncludeImputed
                : ImputedFeaturePolicy.ExcludeImputed);

        return _trainer.TrainAsync(options, cancellationToken);
    }
}
