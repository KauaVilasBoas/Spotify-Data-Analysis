namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>
/// Estratégia de partição treino/teste (padrão Strategy). É o <b>ponto de extensão da DP-4</b>: o MVP separa
/// por faixa (<see cref="SeededHashTrainTestSplitStrategy"/>), porque o conjunto inicial de features é só de
/// áudio e não carrega identidade de artista. Quando o E3.3 trouxer <c>artists.popularity</c>/<c>followers</c>
/// para as features, separar por faixa vira <b>leakage real</b> — faixas do mesmo artista dos dois lados fazem
/// a métrica de teste subir por decoreba de artista, não por generalização — e a migração se resume a trocar
/// a implementação registrada aqui por uma que agrupe por artista.
///
/// <para>Toda implementação DEVE ser determinística: a mesma amostra com a mesma configuração cai sempre no
/// mesmo lado, independentemente da ordem de leitura e de quantas faixas o catálogo tiver.</para>
/// </summary>
public interface ITrainTestSplitStrategy
{
    /// <summary>Decide de que lado a amostra cai.</summary>
    DatasetPartition AssignPartition(TrackTrainingSample sample);
}
