using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations;

/// <summary>
/// Cosine similarity é aritmética pura, então dá para prová-la contra números feitos à mão — e é sobre ela que
/// assentam os testes de sanidade do card: <c>cosine(v, v) = 1</c> e vetores idênticos → ~1.
/// </summary>
public sealed class CosineSimilarityTests
{
    private const double Tolerance = 1e-9;

    private static SimilarityFeatureVector Vector(params double[] coordinates) =>
        SimilarityFeatureVector.Create(coordinates);

    /// <summary>Nove coordenadas com um valor por feature, para montar um vetor válido a partir de uma "semente" escalar.</summary>
    private static SimilarityFeatureVector Uniform(double seed) =>
        SimilarityFeatureVector.Create(Enumerable.Range(1, SimilarityFeatures.Dimension).Select(i => i * seed).ToArray());

    [Fact]
    public void Between_VectorAndItself_IsOne()
    {
        SimilarityFeatureVector vector = Uniform(0.37);

        double similarity = CosineSimilarity.Between(vector, vector);

        Assert.Equal(1.0, similarity, Tolerance);
    }

    [Fact]
    public void Between_IdenticalVectors_IsOne()
    {
        // Faixas "idênticas/quase" → similaridade ~1 (critério de sanidade do card).
        double similarity = CosineSimilarity.Between(Uniform(0.37), Uniform(0.37));

        Assert.Equal(1.0, similarity, Tolerance);
    }

    [Fact]
    public void Between_CollinearVectors_IsOne_RegardlessOfMagnitude()
    {
        // Cosine mede DIREÇÃO: dobrar todas as coordenadas não muda o ângulo, então a similaridade continua 1.
        double similarity = CosineSimilarity.Between(Uniform(1.0), Uniform(2.0));

        Assert.Equal(1.0, similarity, Tolerance);
    }

    [Fact]
    public void Between_OppositeVectors_IsMinusOne()
    {
        double similarity = CosineSimilarity.Between(Uniform(1.0), Uniform(-1.0));

        Assert.Equal(-1.0, similarity, Tolerance);
    }

    [Fact]
    public void Between_OrthogonalVectors_IsZero()
    {
        SimilarityFeatureVector left = Vector(1, 0, 0, 0, 0, 0, 0, 0, 0);
        SimilarityFeatureVector right = Vector(0, 1, 0, 0, 0, 0, 0, 0, 0);

        double similarity = CosineSimilarity.Between(left, right);

        Assert.Equal(0.0, similarity, Tolerance);
    }

    [Fact]
    public void Between_ZeroVector_IsZeroInsteadOfNaN()
    {
        // A origem não tem direção: devolver 0 evita envenenar o ranking com um não-número.
        SimilarityFeatureVector origin = Vector(0, 0, 0, 0, 0, 0, 0, 0, 0);

        double similarity = CosineSimilarity.Between(origin, Uniform(1.0));

        Assert.Equal(0.0, similarity, Tolerance);
        Assert.False(double.IsNaN(similarity));
    }

    [Fact]
    public void Between_KnownAngle_MatchesHandComputedValue()
    {
        // u=(1,1,0,...) e w=(1,0,0,...): dot=1, ‖u‖=√2, ‖w‖=1 → cos = 1/√2.
        SimilarityFeatureVector u = Vector(1, 1, 0, 0, 0, 0, 0, 0, 0);
        SimilarityFeatureVector w = Vector(1, 0, 0, 0, 0, 0, 0, 0, 0);

        double similarity = CosineSimilarity.Between(u, w);

        Assert.Equal(1.0 / Math.Sqrt(2), similarity, Tolerance);
    }
}

/// <summary>
/// O value object do vetor: dimensão fixa, finitude e igualdade estrutural — as invariantes que impedem um vetor
/// desalinhado (a forma silenciosa do skew de ordem) de entrar no cálculo.
/// </summary>
public sealed class SimilarityFeatureVectorTests
{
    [Fact]
    public void Create_WithWrongDimension_Throws()
    {
        Assert.Throws<DomainException>(() => SimilarityFeatureVector.Create([1, 2, 3]));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Create_WithNonFiniteCoordinate_Throws(double bad)
    {
        double[] coordinates = [bad, 0, 0, 0, 0, 0, 0, 0, 0];

        Assert.Throws<DomainException>(() => SimilarityFeatureVector.Create(coordinates));
    }

    [Fact]
    public void Equality_IsStructural()
    {
        double[] coordinates = [0.1, 0.2, 0.3, 120, 0.4, 0.0, 0.2, 0.05, -6.0];

        Assert.Equal(SimilarityFeatureVector.Create(coordinates), SimilarityFeatureVector.Create(coordinates));
    }

    [Fact]
    public void Indexer_ReadsCoordinateByFeature()
    {
        double[] coordinates = [0.1, 0.2, 0.3, 120, 0.4, 0.0, 0.2, 0.05, -6.0];

        SimilarityFeatureVector vector = SimilarityFeatureVector.Create(coordinates);

        Assert.Equal(120, vector[SimilarityFeature.Tempo]);
        Assert.Equal(-6.0, vector[SimilarityFeature.Loudness]);
    }
}
