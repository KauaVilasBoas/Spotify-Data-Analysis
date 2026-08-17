using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

/// <summary>
/// Um ponto no espaço de similaridade: as nove coordenadas contínuas de uma faixa, na ordem canônica de
/// <see cref="SimilarityFeatures.Ordered"/>. Value object imutável com igualdade estrutural — dois vetores com
/// as mesmas coordenadas são o mesmo ponto, o que é exatamente o que os testes de sanidade precisam afirmar.
///
/// <para>É genérico quanto ao SIGNIFICADO das coordenadas de propósito: o MESMO tipo carrega tanto o vetor CRU
/// (unidades originais lidas do catálogo) quanto o vetor NORMALIZADO (z-score). Quem produz o vetor sabe em qual
/// espaço ele está; misturar cru e normalizado no mesmo cálculo é um erro de quem monta o índice, não algo que
/// o VO possa impedir sem carregar uma flag de estado que só serviria para ser esquecida.</para>
/// </summary>
public sealed class SimilarityFeatureVector : ValueObject
{
    private readonly double[] _coordinates;

    private SimilarityFeatureVector(double[] coordinates) => _coordinates = coordinates;

    /// <summary>As coordenadas, na ordem de <see cref="SimilarityFeatures.Ordered"/>. Exposição somente-leitura.</summary>
    public IReadOnlyList<double> Coordinates => _coordinates;

    /// <summary>Acesso posicional por feature, sem depender de o chamador saber o índice numérico.</summary>
    public double this[SimilarityFeature feature] => _coordinates[(int)feature];

    /// <summary>A norma euclidiana (‖v‖). Um vetor de norma zero é a origem — sem direção, e por isso incomparável por cosine.</summary>
    public double Magnitude { get; private init; }

    /// <summary>
    /// Cria o vetor validando dimensão e finitude. A dimensão TEM de ser exatamente
    /// <see cref="SimilarityFeatures.Dimension"/>: um vetor mais curto ou mais longo compararia coordenadas
    /// desalinhadas, que é a forma silenciosa do skew de ordem — por isso falha alto, e não trunca.
    /// </summary>
    /// <exception cref="DomainException">Quando a dimensão diverge ou alguma coordenada é NaN/infinita.</exception>
    public static SimilarityFeatureVector Create(IReadOnlyList<double> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);

        if (coordinates.Count != SimilarityFeatures.Dimension)
            throw new DomainException(
                $"O vetor de similaridade deve ter {SimilarityFeatures.Dimension} coordenadas, na ordem " +
                $"canônica das features contínuas. Recebido: {coordinates.Count}.");

        var copy = new double[coordinates.Count];

        for (int index = 0; index < coordinates.Count; index++)
        {
            double value = coordinates[index];

            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new DomainException(
                    $"Coordenada '{SimilarityFeatures.Ordered[index]}' do vetor de similaridade é inválida " +
                    $"({value}). Todas as coordenadas devem ser números finitos.");

            copy[index] = value;
        }

        return new SimilarityFeatureVector(copy) { Magnitude = EuclideanMagnitude(copy) };
    }

    private static double EuclideanMagnitude(double[] coordinates)
    {
        double sumOfSquares = 0;

        foreach (double coordinate in coordinates)
            sumOfSquares += coordinate * coordinate;

        return Math.Sqrt(sumOfSquares);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (double coordinate in _coordinates)
            yield return coordinate;
    }
}
