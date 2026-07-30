using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>
/// A configuração que torna um dataset de treino <b>reproduzível</b>: a semente do split, a fração reservada
/// para teste e a política de imputação em vigor. Value object imutável e validado — duas execuções com as
/// mesmas opções produzem exatamente os mesmos conjuntos, que é o que permite comparar um treino do E3.2 com
/// o seguinte.
/// </summary>
public sealed class TrainingDatasetSplitOptions : ValueObject
{
    /// <summary>Fração mínima aceitável para o conjunto de teste.</summary>
    public const double MinimumTestFraction = 0.05;

    /// <summary>Fração máxima aceitável para o conjunto de teste.</summary>
    public const double MaximumTestFraction = 0.50;

    private TrainingDatasetSplitOptions(int seed, double testFraction, ImputedFeaturePolicy imputedFeaturePolicy)
    {
        Seed = seed;
        TestFraction = testFraction;
        ImputedFeaturePolicy = imputedFeaturePolicy;
    }

    /// <summary>Semente do split. Fixa por configuração para que reexecuções sejam comparáveis.</summary>
    public int Seed { get; }

    /// <summary>Fração do conjunto elegível reservada para teste, em [0,05; 0,50].</summary>
    public double TestFraction { get; }

    /// <summary>Se as faixas com features imputadas entram no conjunto.</summary>
    public ImputedFeaturePolicy ImputedFeaturePolicy { get; }

    /// <summary>
    /// Cria as opções validando a fração de teste. Fora do intervalo é erro de domínio, não clamp silencioso:
    /// um teste com 1% ou com 90% das faixas não é um split, é um engano — e o consumidor precisa saber.
    /// </summary>
    /// <exception cref="DomainException">Quando a fração de teste está fora de [0,05; 0,50].</exception>
    public static TrainingDatasetSplitOptions Create(
        int seed,
        double testFraction,
        ImputedFeaturePolicy imputedFeaturePolicy = ImputedFeaturePolicy.ExcludeImputed)
    {
        if (double.IsNaN(testFraction) || testFraction < MinimumTestFraction || testFraction > MaximumTestFraction)
            throw new DomainException(
                $"'testFraction' deve estar entre {MinimumTestFraction} e {MaximumTestFraction}. " +
                $"Recebido: {testFraction}.");

        return new TrainingDatasetSplitOptions(seed, testFraction, imputedFeaturePolicy);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Seed;
        yield return TestFraction;
        yield return ImputedFeaturePolicy;
    }
}
