using Microsoft.ML.Data;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

/// <summary>
/// O schema de SAÍDA da predição do ML.NET: a coluna <c>Score</c> que o trainer de regressão emite. Fica na
/// Infrastructure, junto do <see cref="PopularityTrainingRow"/> (o schema de entrada), porque é forma imposta
/// pelo framework — o mesmo motivo pelo qual o ML.NET não atravessa a fronteira do módulo.
/// </summary>
internal sealed class PopularityScoreRow
{
    /// <summary>O score cru da regressão. Nomeado <c>Score</c> por convenção do ML.NET para regressão.</summary>
    [ColumnName("Score")]
    public float Score { get; set; }
}
