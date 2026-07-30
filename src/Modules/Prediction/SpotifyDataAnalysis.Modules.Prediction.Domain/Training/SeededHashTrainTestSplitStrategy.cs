using System.Text;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>
/// Split treino/teste por <b>hash estável da chave da amostra</b> combinado com a semente (DP-4, opção (a):
/// aleatório com semente fixa). Cada faixa é sorteada isoladamente, sem embaralhar a coleção.
///
/// <para>Por que hash e não <c>Random.Shuffle</c> com semente: embaralhar amarra o resultado à ORDEM e à
/// QUANTIDADE de linhas lidas — bastaria o catálogo crescer, ou a leitura vir paginada em lotes diferentes,
/// para os conjuntos mudarem inteiros e um treino deixar de ser comparável com o anterior. Com hash, a
/// pergunta "esta faixa é de treino ou de teste?" é respondida olhando só para a faixa e a semente, o que dá
/// determinismo sob paginação, sob catálogo crescente e sob execução paralela.</para>
///
/// <para>A função é FNV-1a de 64 bits sobre os bytes UTF-8 da chave, semeada. Escolhida por ser estável entre
/// processos e versões do runtime — <see cref="string.GetHashCode()"/> é aleatorizado por processo e
/// produziria conjuntos diferentes a cada execução, exatamente o oposto do que este card exige.</para>
/// </summary>
public sealed class SeededHashTrainTestSplitStrategy : ITrainTestSplitStrategy
{
    private const ulong FnvOffsetBasis = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    /// <summary>Granularidade do sorteio: o hash é reduzido a um balde neste intervalo.</summary>
    private const ulong BucketCount = 1_000_000;

    private readonly int _seed;
    private readonly ulong _testBucketThreshold;

    public SeededHashTrainTestSplitStrategy(TrainingDatasetSplitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _seed = options.Seed;
        _testBucketThreshold = (ulong)Math.Round(options.TestFraction * BucketCount);
    }

    /// <inheritdoc />
    public DatasetPartition AssignPartition(TrackTrainingSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        return ResolveBucket(sample.TrackId) < _testBucketThreshold
            ? DatasetPartition.Test
            : DatasetPartition.Training;
    }

    private ulong ResolveBucket(string splitKey)
    {
        ulong hash = FnvOffsetBasis;

        unchecked
        {
            foreach (byte value in BitConverter.GetBytes(_seed))
            {
                hash = (hash ^ value) * FnvPrime;
            }

            foreach (byte value in Encoding.UTF8.GetBytes(splitKey))
            {
                hash = (hash ^ value) * FnvPrime;
            }
        }

        return hash % BucketCount;
    }
}
