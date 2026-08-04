using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Training;

/// <summary>
/// Porta de leitura das faixas candidatas ao dataset de treino. Entrega o catálogo <b>em fluxo</b>, não uma
/// coleção pronta: quem consome decide o que reter, e o pico de memória do transporte fica preso ao lote, não
/// ao tamanho do catálogo.
///
/// <para>A porta traz o catálogo CRU — inclusive faixas sem popularidade e sem audio-features. Filtrar aqui
/// seria empurrar a regra de elegibilidade para dentro do SQL e perder o motivo de cada exclusão; quem julga
/// é a <see cref="TrainingEligibilitySpecification"/>, no domínio.</para>
/// </summary>
public interface ITrackTrainingCandidateSource
{
    /// <summary>
    /// Percorre todas as faixas do catálogo, em lotes, como candidatas a linha do dataset.
    /// </summary>
    /// <param name="batchSize">Quantas faixas por ida ao banco.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    IAsyncEnumerable<TrackTrainingCandidate> StreamCandidatesAsync(
        int batchSize,
        CancellationToken cancellationToken = default);
}
