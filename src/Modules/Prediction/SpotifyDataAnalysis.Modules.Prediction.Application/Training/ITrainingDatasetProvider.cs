using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Training;

/// <summary>
/// Porta de montagem do dataset de treino. A Application pede o dataset e recebe de volta o <b>censo</b> e a
/// <b>medição</b> da montagem; a materialização em <c>IDataView</c> do ML.NET acontece no adaptador, dentro da
/// Infrastructure do módulo.
///
/// <para>Essa fronteira é o que mantém o ML.NET como serviço externo: a Application (e, por consequência, os
/// casos de uso e o controller) não conhece um único tipo do framework de ML. Trocar ML.NET por outra
/// biblioteca é trocar o adaptador.</para>
/// </summary>
public interface ITrainingDatasetProvider
{
    /// <summary>Monta o dataset com as opções informadas e devolve o censo e a medição da montagem.</summary>
    Task<TrainingDatasetBuildResult> BuildAsync(
        TrainingDatasetSplitOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>O resultado de uma montagem: o que o dataset é, e quanto custou montá-lo.</summary>
/// <param name="Statistics">Censo do dataset — elegíveis, exclusões por motivo e tamanhos das partições.</param>
/// <param name="Measurement">Custo observado da montagem.</param>
public sealed record TrainingDatasetBuildResult(
    TrainingDatasetStatistics Statistics,
    TrainingDatasetBuildMeasurement Measurement);

/// <summary>
/// Custo observado ao montar o dataset. Existe para responder, com número e não com palpite, se o dataset
/// cabe no host da demo — a restrição de free tier (256–512 MB) transforma essa medida em critério de
/// desenho, não em curiosidade.
/// </summary>
/// <param name="ElapsedMilliseconds">Tempo de parede da montagem, do primeiro lote ao dataset fechado.</param>
/// <param name="AllocatedBytes">
/// Bytes alocados no heap gerenciado durante a montagem (acumulado, incluindo o lixo já coletado). Mede a
/// pressão sobre o GC, não o que ficou retido.
/// </param>
/// <param name="RetainedBytes">
/// Crescimento do heap gerenciado após a montagem, medido com coleta forçada. É a aproximação do que o
/// dataset de fato ocupa em memória — o número que decide se cabe no host.
/// </param>
public sealed record TrainingDatasetBuildMeasurement(
    long ElapsedMilliseconds,
    long AllocatedBytes,
    long RetainedBytes);
