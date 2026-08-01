namespace SpotifyDataAnalysis.Modules.Prediction.Contracts.Inference;

/// <summary>
/// A requisição de predição de popularidade (E3.5), na fronteira pública do módulo. POCO achatado: nenhum tipo
/// ML.NET, de domínio ou de EF atravessa aqui — é o contrato que o dashboard (E5.3) e o Swagger consomem.
///
/// <para>Os dois modos são mutuamente exclusivos (DP-1): informe <see cref="TrackId"/> <b>ou</b>
/// <see cref="Features"/>, nunca os dois, nunca nenhum. A validação disso é 400 (ProblemDetails), não
/// comportamento surpresa — a invariante é aplicada no domínio e reforçada por FluentValidation.</para>
/// </summary>
public sealed class PopularityPredictionRequest
{
    /// <summary>Modo <c>trackId</c>: prever pela faixa do catálogo. Deixe nulo ao usar <see cref="Features"/>.</summary>
    public string? TrackId { get; init; }

    /// <summary>Modo features: prever por um bloco informado. Deixe nulo ao usar <see cref="TrackId"/>.</summary>
    public AudioFeaturesPayload? Features { get; init; }
}

/// <summary>
/// O bloco de features do modo "features à mão". Reflete <b>exatamente</b> o feature set do modelo corrente
/// (E3.2): as nove grandezas contínuas de áudio mais duração e explícito. Sem gênero/artista/Key — o pipeline
/// campeão ainda não os consome (o E3.3 não foi para produção). Quando o feature set mudar, este payload
/// acompanha, e o <c>GET /api/model/current</c> continua publicando a lista exata que o cliente deve enviar.
/// </summary>
public sealed class AudioFeaturesPayload
{
    public double Danceability { get; init; }
    public double Energy { get; init; }
    public double Valence { get; init; }
    public double Tempo { get; init; }
    public double Acousticness { get; init; }
    public double Instrumentalness { get; init; }
    public double Liveness { get; init; }
    public double Speechiness { get; init; }
    public double Loudness { get; init; }
    public int DurationMs { get; init; }
    public bool Explicit { get; init; }
}

/// <summary>
/// A resposta da predição: a popularidade prevista (0–100), a versão do modelo que respondeu, o modo usado e os
/// avisos de qualidade do insumo/saída. Os avisos existem porque a honestidade é regra do épico — imputação
/// nunca passa como medida em silêncio, e uma saída que extrapolou o domínio é sinalizada, não maquiada.
/// </summary>
public sealed class PopularityPredictionResponse
{
    /// <summary>Popularidade prevista, garantidamente em [0, 100].</summary>
    public double PredictedPopularity { get; init; }

    /// <summary>O score cru do modelo antes do clamp — transparência sobre o que ele realmente emitiu.</summary>
    public double RawScore { get; init; }

    /// <summary>Se a saída extrapolou [0, 100] e foi limitada.</summary>
    public bool WasClamped { get; init; }

    /// <summary>Versão do modelo corrente que respondeu esta predição.</summary>
    public int ModelVersion { get; init; }

    /// <summary>Modo usado: <c>trackId</c> ou <c>features</c>.</summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>
    /// Avisos de qualidade sobre esta predição (features imputadas, saída clampada). Lista vazia significa uma
    /// predição sobre insumo medido, dentro do domínio — o caso limpo.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
