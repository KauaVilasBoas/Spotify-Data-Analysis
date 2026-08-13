namespace SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;

/// <summary>
/// Lê do schema <c>catalog</c> os METADADOS de exibição das faixas envolvidas numa recomendação (E4.2): nome,
/// artista principal, álbum e gênero. É a mesma fronteira do E3.1/E4.1 — o Prediction lê o catálogo por SQL, sem
/// referenciar tipo algum do Catalog (acoplamento por DADO, guardado pelos ArchTests).
///
/// <para>Separada de <see cref="ISimilarityFeatureSource"/> de propósito: aquela lê as features (o insumo do
/// índice, varrido uma vez e cacheado); esta lê os rótulos legíveis de um PUNHADO de faixas (a semente e as
/// top-N), buscados por id a cada request. Misturar rótulo de UI no índice inflaria o cache com dado que só
/// interessa às poucas faixas de uma resposta.</para>
/// </summary>
public interface ITrackMetadataSource
{
    /// <summary>
    /// Busca a existência, o estado das features e os metadados de UMA faixa por id. Devolve <c>null</c> quando a
    /// faixa não existe no catálogo — o que distingue 404 (inexistente) de 422 (existe, mas sem features), decidido
    /// pelo chamador a partir de <see cref="TrackMetadataRow.HasCompleteFeatures"/>.
    /// </summary>
    Task<TrackMetadataRow?> FindByTrackIdAsync(string trackId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca os metadados de um CONJUNTO de faixas por id, numa única leitura. Usado para hidratar as top-N
    /// recomendações com nome/artista/álbum/gênero sem uma ida ao banco por faixa. A ordem do retorno não é
    /// garantida — o chamador casa por id.
    /// </summary>
    Task<IReadOnlyDictionary<string, TrackMetadataRow>> FindByTrackIdsAsync(
        IReadOnlyCollection<string> trackIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// Os metadados de exibição de uma faixa do catálogo, mais os sinais que decidem os caminhos de erro da semente.
/// <see cref="HasAudioFeatures"/>/<see cref="HasCompleteFeatures"/> permitem ao handler distinguir "faixa existe
/// mas não dá para recomendar" (422) de "faixa não existe" (null → 404). <see cref="IsImputed"/> viaja para a
/// sinalização da DP-F.
/// </summary>
/// <param name="TrackId">Id da faixa no Spotify.</param>
/// <param name="Name">Nome da faixa.</param>
/// <param name="Artist">Artista principal (1º crédito do array jsonb <c>artists</c>), quando presente.</param>
/// <param name="Album">Nome do álbum (via <c>catalog.albums</c>), quando registrado.</param>
/// <param name="Genre">Gênero das audio-features, quando a faixa tem features.</param>
/// <param name="Popularity">Popularidade da faixa (0–100) — o 1º critério do representante no dedup do E4.7.</param>
/// <param name="HasAudioFeatures">Se a faixa tem o jsonb <c>audio_features</c> não nulo.</param>
/// <param name="IsImputed">Se as audio-features foram imputadas, não medidas (DP-F).</param>
/// <param name="HasCompleteFeatures">Se as nove features contínuas do vetor de similaridade estão presentes.</param>
public sealed record TrackMetadataRow(
    string TrackId,
    string? Name,
    string? Artist,
    string? Album,
    string? Genre,
    int Popularity,
    bool HasAudioFeatures,
    bool IsImputed,
    bool HasCompleteFeatures);
