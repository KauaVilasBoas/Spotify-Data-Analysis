using FluentValidation;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;

/// <summary>
/// Valida os parâmetros da recomendação na fronteira HTTP, para um pedido malformado virar 400 (ProblemDetails
/// RFC 7807) em vez de 500 ou de uma resposta sem sentido. Roda no <c>ValidationBehavior</c> do mediator, antes do
/// handler.
///
/// <para>Os limites são as MESMAS constantes de <see cref="GetTrackRecommendationsQuery"/> — a validação de
/// fronteira e o clamp de defesa em profundidade do handler leem os mesmos números, então não podem divergir. O
/// <c>seedTrackId</c> vem da rota (nunca nulo aqui), mas exigi-lo não-vazio protege contra uma rota degenerada.</para>
/// </summary>
public sealed class GetTrackRecommendationsQueryValidator : AbstractValidator<GetTrackRecommendationsQuery>
{
    public GetTrackRecommendationsQueryValidator()
    {
        RuleFor(query => query.SeedTrackId)
            .NotEmpty()
            .OverridePropertyName("id")
            .WithMessage("O id da faixa-semente é obrigatório.");

        RuleFor(query => query.Limit)
            .InclusiveBetween(1, GetTrackRecommendationsQuery.MaximumLimit)
            .OverridePropertyName("limit")
            .WithMessage(
                $"'limit' deve estar entre 1 e {GetTrackRecommendationsQuery.MaximumLimit}. " +
                $"Sem informar, vale o padrão de {GetTrackRecommendationsQuery.DefaultLimit}.");

        RuleFor(query => query.ExplainTopK)
            .InclusiveBetween(1, GetTrackRecommendationsQuery.MaximumExplainTopK)
            .OverridePropertyName("explainTopK")
            .WithMessage(
                $"'explainTopK' deve estar entre 1 e {GetTrackRecommendationsQuery.MaximumExplainTopK} " +
                "(a dimensão do vetor de similaridade). " +
                $"Sem informar, vale o padrão de {GetTrackRecommendationsQuery.DefaultExplainTopK}.");

        // Um genreMode fora do enum (ex.: ?genreMode=99 ou um nome inexistente que o binder não resolveu) vira 400,
        // não um comportamento default silencioso — o cliente deve saber que o modo pedido não existe.
        RuleFor(query => query.GenreMode)
            .IsInEnum()
            .OverridePropertyName("genreMode")
            .WithMessage(
                "'genreMode' deve ser um de: boost (default, gênero pesa no ranking), off (cosine puro) ou " +
                "sameGenreOnly (só o mesmo gênero da semente).");

        RuleFor(query => query.Strategy)
            .IsInEnum()
            .OverridePropertyName("strategy")
            .WithMessage("'strategy' deve ser um de: content (default) ou blend (mistura o colaborativo).");

        // Peso fora de [0, 1] vira 400: um blendWeight negativo ou > 1 não tem sentido e o domínio o rejeitaria
        // com 500. A validação de fronteira lê o mesmo intervalo do RecommendationBlender.
        RuleFor(query => query.BlendWeight)
            .InclusiveBetween(0.0, 1.0)
            .OverridePropertyName("blendWeight")
            .WithMessage("'blendWeight' deve estar entre 0 e 1. Sem informar, vale o padrão de 0,35.");
    }
}
