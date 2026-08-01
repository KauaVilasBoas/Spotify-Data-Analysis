using FluentValidation;
using SpotifyDataAnalysis.Modules.Prediction.Contracts.Inference;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Inference;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Inference;

/// <summary>
/// Valida a FORMA do request de predição na fronteira HTTP, para que uma mensagem malformada vire 400
/// (ProblemDetails RFC 7807) em vez de 500 ou de uma predição sem sentido. Roda no <c>ValidationBehavior</c>
/// do mediator, antes do handler.
///
/// <para>Duas responsabilidades: a invariante XOR da DP-1 (um e apenas um modo) e a faixa de valores das
/// features no modo "à mão". Os limites são as MESMAS constantes do value object <see cref="AudioFeatureInput"/>
/// — a validação de fronteira e a invariante de domínio não podem divergir, então há uma fonte só para os
/// números. O domínio revalida como defesa em profundidade; aqui o objetivo é o status code certo e a mensagem
/// por campo que o cliente precisa para corrigir.</para>
/// </summary>
public sealed class PredictPopularityCommandValidator : AbstractValidator<PredictPopularityCommand>
{
    public PredictPopularityCommandValidator()
    {
        RuleFor(command => command.Request)
            .NotNull()
            .WithMessage("O corpo da requisição de predição é obrigatório.");

        RuleFor(command => command.Request)
            .Must(ExactlyOneModeInformed)
            // OverridePropertyName (e não WithName): na v11, WithName troca só o display name da mensagem,
            // mas o ExceptionHandlingMiddleware agrupa o ProblemDetails por PropertyName. É este que precisa
            // dizer "mode" para o cliente saber onde está o erro.
            .OverridePropertyName("mode")
            .WithMessage(
                "Informe exatamente um modo de predição: 'trackId' de uma faixa do catálogo OU um bloco de " +
                "'features', nunca os dois e nunca nenhum.")
            .When(command => command.Request is not null);

        // As regras de faixa só se aplicam quando o cliente escolheu o modo features. No modo trackId, as
        // features vêm do catálogo e não são validadas contra estes limites (já foram medidas/imputadas lá).
        When(command => command.Request?.Features is not null, () =>
        {
            RuleFor(command => command.Request!.Features!.Danceability)
                .InclusiveBetween(0.0, 1.0).OverridePropertyName("features.danceability");
            RuleFor(command => command.Request!.Features!.Energy)
                .InclusiveBetween(0.0, 1.0).OverridePropertyName("features.energy");
            RuleFor(command => command.Request!.Features!.Valence)
                .InclusiveBetween(0.0, 1.0).OverridePropertyName("features.valence");
            RuleFor(command => command.Request!.Features!.Acousticness)
                .InclusiveBetween(0.0, 1.0).OverridePropertyName("features.acousticness");
            RuleFor(command => command.Request!.Features!.Instrumentalness)
                .InclusiveBetween(0.0, 1.0).OverridePropertyName("features.instrumentalness");
            RuleFor(command => command.Request!.Features!.Liveness)
                .InclusiveBetween(0.0, 1.0).OverridePropertyName("features.liveness");
            RuleFor(command => command.Request!.Features!.Speechiness)
                .InclusiveBetween(0.0, 1.0).OverridePropertyName("features.speechiness");

            RuleFor(command => command.Request!.Features!.Tempo)
                .InclusiveBetween(AudioFeatureInput.MinimumTempo, AudioFeatureInput.MaximumTempo)
                .OverridePropertyName("features.tempo");
            RuleFor(command => command.Request!.Features!.Loudness)
                .InclusiveBetween(AudioFeatureInput.MinimumLoudness, AudioFeatureInput.MaximumLoudness)
                .OverridePropertyName("features.loudness");
            RuleFor(command => command.Request!.Features!.DurationMs)
                .InclusiveBetween(AudioFeatureInput.MinimumDurationMs, AudioFeatureInput.MaximumDurationMs)
                .OverridePropertyName("features.durationMs");
        });
    }

    /// <summary>A invariante XOR: exatamente um dos dois campos vem informado.</summary>
    private static bool ExactlyOneModeInformed(PopularityPredictionRequest request)
    {
        bool hasTrackId = !string.IsNullOrWhiteSpace(request.TrackId);
        bool hasFeatures = request.Features is not null;

        return hasTrackId ^ hasFeatures;
    }
}
