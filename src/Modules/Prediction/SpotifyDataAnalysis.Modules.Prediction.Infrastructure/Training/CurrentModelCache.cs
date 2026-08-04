using Microsoft.ML;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Models;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

/// <summary>
/// Cache em memória do modelo corrente já desserializado.
///
/// <para>Desserializar o <c>.zip</c> a cada requisição inviabilizaria a inferência do E3.5 — o custo é de
/// carga, não de predição. O cache é singleton e guarda também o número da versão carregada, de modo que uma
/// promoção nova possa invalidá-lo <b>sem reiniciar a aplicação</b>.</para>
/// </summary>
internal sealed class CurrentModelCache
{
    private readonly MLContext _mlContext;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ITransformer? _model;
    private int? _loadedVersion;

    public CurrentModelCache(MLContext mlContext) => _mlContext = mlContext;

    /// <summary>Versão atualmente carregada, ou nula se o cache está frio.</summary>
    public int? LoadedVersion => _loadedVersion;

    /// <summary>
    /// Devolve o modelo da versão informada, carregando-o do artefato apenas quando o cache está frio ou
    /// aponta para outra versão. O semáforo evita que duas requisições simultâneas desserializem o mesmo
    /// artefato em paralelo no primeiro acesso.
    /// </summary>
    public async Task<ITransformer> GetOrLoadAsync(
        ModelVersion version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (_model is not null && _loadedVersion == version.Id)
            return _model;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_model is not null && _loadedVersion == version.Id)
                return _model;

            using var stream = new MemoryStream(version.Artifact);
            _model = _mlContext.Model.Load(stream, out _);
            _loadedVersion = version.Id;

            return _model;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Esquece o modelo carregado. Chamado ao promover uma versão — a próxima leitura recarrega do banco, sem
    /// reinício da aplicação.
    /// </summary>
    public void Invalidate()
    {
        _model = null;
        _loadedVersion = null;
    }
}
