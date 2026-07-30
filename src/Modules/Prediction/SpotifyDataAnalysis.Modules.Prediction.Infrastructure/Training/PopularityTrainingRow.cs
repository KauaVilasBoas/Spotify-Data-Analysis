using Microsoft.ML.Data;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

/// <summary>
/// O schema de entrada do ML.NET: a tradução de <see cref="TrackTrainingSample"/> para o formato que o
/// <c>IDataView</c> entende (<see cref="float"/> em vez de <see cref="double"/>, alvo nomeado
/// <c>Label</c>). Fica na Infrastructure porque é forma imposta pelo framework, não conceito de domínio.
///
/// <para><b>Não existe coluna de imputação aqui, e isso é proposital</b> (DP-2): expor <c>IsImputed</c> ao
/// modelo lhe daria a chave para isolar o artefato da imputação e acertar pelo motivo errado. A flag existe
/// no domínio para RELATAR a composição dos conjuntos, não para o modelo consumir.</para>
///
/// <para><see cref="Key"/>, <see cref="Mode"/> e <see cref="TimeSignature"/> viajam junto embora não sejam
/// contínuas: elas não são descartadas, apenas ganham codificação própria no E3.3. <see cref="TrackId"/>
/// viaja como rastro para inspecionar predições, e não como feature — quem escolhe as colunas do vetor de
/// features é o pipeline de treino do E3.2.</para>
/// </summary>
internal sealed class PopularityTrainingRow
{
    public string TrackId { get; set; } = string.Empty;

    /// <summary>Alvo da regressão. Nomeado <c>Label</c> por convenção do ML.NET.</summary>
    [ColumnName("Label")]
    public float Popularity { get; set; }

    public float Danceability { get; set; }
    public float Energy { get; set; }
    public float Valence { get; set; }
    public float Tempo { get; set; }
    public float Acousticness { get; set; }
    public float Instrumentalness { get; set; }
    public float Liveness { get; set; }
    public float Speechiness { get; set; }
    public float Loudness { get; set; }

    public float Key { get; set; }
    public float Mode { get; set; }
    public float TimeSignature { get; set; }

    public string Genre { get; set; } = string.Empty;

    /// <summary>Traduz uma amostra do domínio para a linha do ML.NET.</summary>
    public static PopularityTrainingRow FromSample(TrackTrainingSample sample) => new()
    {
        TrackId = sample.TrackId,
        Popularity = sample.Popularity,
        Danceability = (float)sample.Danceability,
        Energy = (float)sample.Energy,
        Valence = (float)sample.Valence,
        Tempo = (float)sample.Tempo,
        Acousticness = (float)sample.Acousticness,
        Instrumentalness = (float)sample.Instrumentalness,
        Liveness = (float)sample.Liveness,
        Speechiness = (float)sample.Speechiness,
        Loudness = (float)sample.Loudness,
        Key = sample.Key,
        Mode = sample.Mode,
        TimeSignature = sample.TimeSignature,
        Genre = sample.Genre ?? string.Empty
    };
}
