using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;

/// <summary>
/// Lê do schema <c>catalog</c> as faixas ELEGÍVEIS para o índice de similaridade: só as que têm as nove features
/// contínuas presentes. É a mesma fronteira do E3.1 — o Prediction lê o catálogo por SQL, sem referenciar tipo
/// algum do Catalog (acoplamento por DADO, guardado pelos ArchTests).
///
/// <para>Devolve <see cref="RawTrackFeatures"/> do domínio (vetor CRU + marca de imputação): a normalização é
/// responsabilidade de <see cref="SimilarityIndex.Build"/>, não da leitura. Faixa sem <c>audio_features</c>
/// completo nunca é emitida — a elegibilidade mora no <c>WHERE</c> do SQL, para uma faixa sem insumo jamais
/// entrar no índice em silêncio.</para>
/// </summary>
public interface ISimilarityFeatureSource
{
    /// <summary>
    /// Streama, em lotes por keyset, as faixas elegíveis com seus vetores crus. É varredura de catálogo inteiro
    /// (para montar o índice uma vez), não busca pontual — daí o streaming, que limita o pico de memória do
    /// transporte ao tamanho do lote.
    /// </summary>
    IAsyncEnumerable<RawTrackFeatures> StreamEligibleTracksAsync(
        int batchSize, CancellationToken cancellationToken = default);
}
