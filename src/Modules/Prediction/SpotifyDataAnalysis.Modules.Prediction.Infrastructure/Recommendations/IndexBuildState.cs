namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Recommendations;

/// <summary>
/// Estado explícito do ciclo de vida da montagem do índice de similaridade.
/// Publicado por <see cref="CachedTrackSimilarityIndexProvider"/> antes de qualquer await, garantindo
/// visibilidade entre threads sem depender do contador interno do <see cref="System.Threading.SemaphoreSlim"/>.
/// </summary>
internal enum IndexBuildState
{
    /// <summary>Nenhuma montagem foi iniciada ainda.</summary>
    NotStarted,

    /// <summary>A varredura do catálogo está em andamento.</summary>
    Building,

    /// <summary>O índice está pronto para consulta.</summary>
    Ready,

    /// <summary>A última tentativa de montagem terminou em exceção; a próxima chamada tentará novamente.</summary>
    Failed,
}
