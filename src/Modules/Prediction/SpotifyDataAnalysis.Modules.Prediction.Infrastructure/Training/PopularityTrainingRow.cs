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

    /// <summary>Duração em milissegundos. Escala completamente diferente das features 0–1, por isso a
    /// normalização no pipeline não é opcional.</summary>
    public float DurationMs { get; set; }

    /// <summary>Explícita como 0/1 — o ML.NET consome o vetor de features em <see cref="float"/>.</summary>
    public float Explicit { get; set; }

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

    /// <summary>
    /// Traduz uma amostra do domínio (treino) para a linha do ML.NET. Delega a montagem das FEATURES a
    /// <see cref="FromAudioFeatures"/> — a mesma rotina que a inferência (E3.5) usa —, e só acrescenta o alvo
    /// <c>Popularity</c> e os metadados de rastro. Compartilhar a montagem é o que fecha, por construção, o
    /// buraco do skew treino/inferência: não há duas traduções que possam divergir.
    /// </summary>
    public static PopularityTrainingRow FromSample(TrackTrainingSample sample)
    {
        PopularityTrainingRow row = FromAudioFeatures(
            sample.Danceability,
            sample.Energy,
            sample.Valence,
            sample.Tempo,
            sample.Acousticness,
            sample.Instrumentalness,
            sample.Liveness,
            sample.Speechiness,
            sample.Loudness,
            sample.DurationMs,
            sample.Explicit);

        row.TrackId = sample.TrackId;
        row.Popularity = sample.Popularity;

        // Key/Mode/TimeSignature/Genre viajam para futura codificação (E3.3) mas não entram no vetor de
        // features do pipeline atual — por isso não fazem parte de FromAudioFeatures, que é o insumo da predição.
        row.Key = sample.Key;
        row.Mode = sample.Mode;
        row.TimeSignature = sample.TimeSignature;
        row.Genre = sample.Genre ?? string.Empty;

        return row;
    }

    /// <summary>
    /// Monta a linha do ML.NET a partir <b>apenas</b> das features que o pipeline campeão consome (as 9
    /// grandezas contínuas de áudio mais duração e explícito). É o único caminho de tradução das features, usado
    /// tanto pelo treino quanto pela inferência: a conversão <see cref="double"/> → <see cref="float"/> e a
    /// codificação de <c>explicit</c> como 0/1 acontecem aqui, e em lugar nenhum além daqui.
    /// </summary>
    public static PopularityTrainingRow FromAudioFeatures(
        double danceability,
        double energy,
        double valence,
        double tempo,
        double acousticness,
        double instrumentalness,
        double liveness,
        double speechiness,
        double loudness,
        int durationMs,
        bool @explicit) => new()
    {
        DurationMs = durationMs,
        Explicit = @explicit ? 1f : 0f,
        Danceability = (float)danceability,
        Energy = (float)energy,
        Valence = (float)valence,
        Tempo = (float)tempo,
        Acousticness = (float)acousticness,
        Instrumentalness = (float)instrumentalness,
        Liveness = (float)liveness,
        Speechiness = (float)speechiness,
        Loudness = (float)loudness
    };
}
