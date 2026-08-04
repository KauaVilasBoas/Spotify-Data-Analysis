using System.Collections.Concurrent;
using Microsoft.ML;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Inference;

/// <summary>
/// Pool próprio de <see cref="PredictionEngine{TSrc,TDst}"/> para servir a inferência (E3.5, decisão A do fork).
///
/// <para><b>Por que um pool próprio e não o <c>PredictionEnginePool</c> de <c>Microsoft.Extensions.ML</c>:</b>
/// o pool oficial carrega o modelo de arquivo/URI, e a fonte da verdade do artefato neste projeto é o banco
/// (<c>bytea</c>, decisão E3.4). Forçar o pool oficial exigiria um round-trip do binário para o disco a cada
/// promoção. Este pool reusa o <see cref="ITransformer"/> já desserializado pelo <c>CurrentModelCache</c>,
/// mantendo o modelo no banco como fonte única.</para>
///
/// <para><b>Por que um pool e não um engine por request:</b> o <see cref="PredictionEngine{TSrc,TDst}"/> NÃO é
/// thread-safe — criar um por request é o erro clássico de servir ML.NET em ASP.NET (custo de criação alto e,
/// sob concorrência, resultado corrompido). O pool empresta um engine por operação e o devolve, então cada
/// engine é usado por uma thread de cada vez, e engines são reaproveitados entre requisições.</para>
///
/// <para><b>Invalidação por versão:</b> o pool carrega a identidade da versão que produziu seus engines. Quando
/// uma promoção troca o modelo corrente, a próxima locação com uma versão diferente <b>drena</b> os engines
/// antigos e passa a criar engines do novo <see cref="ITransformer"/> — sem reiniciar a aplicação, espelhando a
/// invalidação do <c>CurrentModelCache</c>.</para>
///
/// <para>Singleton: o estado (engines reaproveitados) só faz sentido se sobreviver entre requisições.</para>
/// </summary>
internal sealed class PopularityPredictionEnginePool : IDisposable
{
    private readonly MLContext _mlContext;
    private readonly ConcurrentQueue<PredictionEngine<PopularityTrainingRow, PopularityScoreRow>> _engines = new();
    private readonly object _versionGate = new();

    private ITransformer? _model;
    private int? _pooledVersion;

    public PopularityPredictionEnginePool(MLContext mlContext) => _mlContext = mlContext;

    /// <summary>
    /// Empresta um engine para a versão informada, garantindo que ele foi construído a partir do
    /// <paramref name="model"/> daquela versão. Devolver é responsabilidade do <see cref="EngineLease"/>
    /// (padrão RAII): use com <c>using</c> para que o engine sempre volte ao pool.
    /// </summary>
    public EngineLease Rent(int version, ITransformer model)
    {
        ArgumentNullException.ThrowIfNull(model);

        EnsurePoolMatchesVersion(version, model);

        if (!_engines.TryDequeue(out PredictionEngine<PopularityTrainingRow, PopularityScoreRow>? engine))
            engine = CreateEngine(model);

        return new EngineLease(this, engine);
    }

    /// <summary>
    /// Sincroniza o pool com a versão corrente. Só toma o lock quando a versão mudou — o caminho quente (mesma
    /// versão a cada request) faz apenas uma leitura de campo, sem contenção.
    /// </summary>
    private void EnsurePoolMatchesVersion(int version, ITransformer model)
    {
        if (_pooledVersion == version)
            return;

        lock (_versionGate)
        {
            if (_pooledVersion == version)
                return;

            DrainEngines();

            _model = model;
            _pooledVersion = version;
        }
    }

    /// <summary>Descarta os engines da versão anterior. Cada engine é <see cref="IDisposable"/>.</summary>
    private void DrainEngines()
    {
        while (_engines.TryDequeue(out PredictionEngine<PopularityTrainingRow, PopularityScoreRow>? stale))
            stale.Dispose();
    }

    private PredictionEngine<PopularityTrainingRow, PopularityScoreRow> CreateEngine(ITransformer model) =>
        _mlContext.Model.CreatePredictionEngine<PopularityTrainingRow, PopularityScoreRow>(model);

    /// <summary>
    /// Devolve um engine ao pool, mas só se ele ainda pertence à versão corrente — um engine de uma versão já
    /// substituída por promoção é descartado em vez de reintroduzido, para não contaminar o pool novo.
    /// </summary>
    private void ReturnOrDispose(
        int version, PredictionEngine<PopularityTrainingRow, PopularityScoreRow> engine)
    {
        if (_pooledVersion == version)
            _engines.Enqueue(engine);
        else
            engine.Dispose();
    }

    public void Dispose() => DrainEngines();

    /// <summary>
    /// A concessão de um engine ao chamador. Ao ser descartada, devolve o engine ao pool (ou o descarta, se a
    /// versão virou). Não é thread-safe de propósito: uma concessão pertence a um fluxo de execução só, o mesmo
    /// que a garantia de thread-safety do pool exige.
    /// </summary>
    internal readonly struct EngineLease : IDisposable
    {
        private readonly PopularityPredictionEnginePool _pool;
        private readonly int _version;

        internal EngineLease(
            PopularityPredictionEnginePool pool,
            PredictionEngine<PopularityTrainingRow, PopularityScoreRow> engine)
        {
            _pool = pool;
            _version = pool._pooledVersion!.Value;
            Engine = engine;
        }

        /// <summary>O engine emprestado — válido apenas durante a vida desta concessão.</summary>
        public PredictionEngine<PopularityTrainingRow, PopularityScoreRow> Engine { get; }

        public void Dispose() => _pool.ReturnOrDispose(_version, Engine);
    }
}
