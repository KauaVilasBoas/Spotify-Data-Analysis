using FluentValidation.Results;
using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations;

/// <summary>
/// A validação de fronteira do endpoint público (E4.2): <c>limit</c> ∈ [1, 50] e <c>explainTopK</c> ∈ [1, 9] —
/// fora disso é 400 (ProblemDetails), não 500 nem clamp silencioso. O <c>PropertyName</c> tem de nomear o
/// parâmetro certo, porque é por ele que o middleware agrupa o ProblemDetails.
/// </summary>
public sealed class GetTrackRecommendationsQueryValidatorTests
{
    private static readonly GetTrackRecommendationsQueryValidator Validator = new();

    private static ValidationResult Validate(int limit, int explainTopK) =>
        Validator.Validate(new GetTrackRecommendationsQuery("seed", limit, explainTopK));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 3)]
    [InlineData(50, 9)]
    public void ValidBounds_Pass(int limit, int explainTopK)
    {
        Assert.True(Validate(limit, explainTopK).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(51)]
    [InlineData(1000)]
    public void LimitOutOfRange_FailsOnLimit(int limit)
    {
        ValidationResult result = Validate(limit, explainTopK: 3);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "limit");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10)]
    public void ExplainTopKOutOfRange_FailsOnExplainTopK(int explainTopK)
    {
        ValidationResult result = Validate(limit: 10, explainTopK);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "explainTopK");
    }

    [Fact]
    public void EmptySeedTrackId_FailsOnId()
    {
        ValidationResult result = Validator.Validate(new GetTrackRecommendationsQuery("  ", 10, 3));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "id");
    }
}
