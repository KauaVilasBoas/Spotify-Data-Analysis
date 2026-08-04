using SpotifyDataAnalysis.SharedKernel.Domain;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Inference;

/// <summary>
/// A popularidade prevista pelo modelo, já presa ao domínio do alvo [0, 100].
///
/// <para>Uma regressão não conhece o teto do que prevê: pode devolver 103 para uma faixa muito popular ou -4
/// para uma obscura. Devolver esses números ao cliente destrói a credibilidade da demo e não significa nada —
/// popularidade é uma escala fechada. O clamp é regra de domínio, não formatação de apresentação, por isso vive
/// aqui e não no controller: <see cref="WasClamped"/> preserva a honestidade de sinalizar quando a saída crua
/// caiu fora, em vez de esconder a extrapolação.</para>
/// </summary>
public sealed class PredictedPopularity : ValueObject
{
    /// <summary>Piso e teto da escala de popularidade do Spotify.</summary>
    public const double Minimum = 0.0;
    public const double Maximum = 100.0;

    private PredictedPopularity(double value, double rawScore, bool wasClamped)
    {
        Value = value;
        RawScore = rawScore;
        WasClamped = wasClamped;
    }

    /// <summary>A popularidade prevista, garantidamente em [0, 100].</summary>
    public double Value { get; }

    /// <summary>O score cru que o modelo emitiu, antes do clamp — guardado para transparência e diagnóstico.</summary>
    public double RawScore { get; }

    /// <summary>Se o score cru extrapolou o domínio e precisou ser limitado.</summary>
    public bool WasClamped { get; }

    /// <summary>
    /// Envolve um score cru do modelo, limitando-o a [0, 100]. Um <c>NaN</c> vira o piso (0) e é marcado como
    /// clampado: um modelo que emite <c>NaN</c> por um insumo degenerado não pode derrubar a resposta, mas
    /// tampouco pode passar por uma predição legítima.
    /// </summary>
    public static PredictedPopularity FromRawScore(double rawScore)
    {
        if (double.IsNaN(rawScore))
            return new PredictedPopularity(Minimum, rawScore, wasClamped: true);

        double clamped = Math.Clamp(rawScore, Minimum, Maximum);

        // A comparação é exata de propósito: só é "clampado" quando o próprio Clamp trocou o valor, o que só
        // acontece quando o cru estava estritamente fora da faixa.
        return new PredictedPopularity(clamped, rawScore, wasClamped: clamped != rawScore);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
        yield return RawScore;
        yield return WasClamped;
    }
}
