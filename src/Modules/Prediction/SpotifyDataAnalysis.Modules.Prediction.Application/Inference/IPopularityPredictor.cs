using SpotifyDataAnalysis.Modules.Prediction.Domain.Inference;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Inference;

/// <summary>
/// Porta de inferência: dado um bloco de features já validado, devolve a popularidade prevista pelo modelo
/// corrente. É o ponto por onde o ML.NET entra sem atravessar a fronteira — a Application enxerga só esta
/// interface, a implementação (pool de engines sobre o modelo cacheado) vive na Infrastructure do módulo.
///
/// <para>Recebe um <see cref="AudioFeatureInput"/> do domínio, e não o payload cru: quem monta o vetor de
/// features é sempre o mesmo caminho do treino (<c>PopularityTrainingRow.FromSample</c>), e é essa unicidade
/// que fecha o buraco do skew treino/inferência. A resolução <c>trackId → features</c> acontece antes, no
/// handler, para que a inferência não conheça o catálogo.</para>
/// </summary>
public interface IPopularityPredictor
{
    /// <summary>
    /// Prediz a popularidade para o bloco informado. Falha alto (<see cref="Domain.Models"/> /
    /// <c>NoCurrentModelException</c>) quando não há modelo corrente publicado — nunca devolve um valor default.
    /// </summary>
    Task<PopularityPrediction> PredictAsync(
        AudioFeatureInput features, CancellationToken cancellationToken = default);
}

/// <summary>
/// O resultado de uma inferência: a popularidade prevista (já presa a [0, 100]) e a versão do modelo que
/// respondeu. A versão viaja junto porque uma predição sem a proveniência do modelo não é auditável — é o
/// mesmo princípio do versionamento do E3.4.
/// </summary>
public sealed record PopularityPrediction(PredictedPopularity Popularity, int ModelVersion);
