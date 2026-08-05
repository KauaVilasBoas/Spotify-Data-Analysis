using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;

/// <summary>
/// Dá acesso ao <see cref="SimilarityIndex"/> do catálogo, montado UMA vez e reusado entre requisições (DP-D:
/// on-the-fly com cache do índice normalizado). Montar o índice varre ~90k faixas e aprende μ/σ — caro demais
/// para refazer por request; barato de guardar (~7 MB). É o mesmo princípio do cache do modelo corrente do E3.5.
///
/// <para>Porta na Application; o cache com estado (singleton, thread-safe) vive na Infrastructure — a Application
/// só declara "me dê o índice pronto", sem saber como ele é montado ou por quanto tempo vive.</para>
/// </summary>
public interface ITrackSimilarityIndexProvider
{
    /// <summary>
    /// Devolve o índice pronto, montando-o na primeira chamada e reaproveitando-o depois. Chamadas concorrentes
    /// durante a montagem inicial aguardam a mesma montagem, em vez de disparar várias varreduras do catálogo.
    /// </summary>
    Task<SimilarityIndex> GetIndexAsync(CancellationToken cancellationToken = default);
}
