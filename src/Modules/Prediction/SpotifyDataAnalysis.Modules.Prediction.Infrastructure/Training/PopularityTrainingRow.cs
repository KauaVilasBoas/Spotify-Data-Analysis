using System.Globalization;
using Microsoft.ML.Data;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

/// <summary>
/// O schema de entrada do ML.NET: a tradução de <see cref="TrackTrainingSample"/> para o formato que o
/// <c>IDataView</c> entende (<see cref="float"/> em vez de <see cref="double"/>, alvo nomeado
/// <c>Label</c>). Fica na Infrastructure porque é forma imposta pelo framework, não conceito de domínio.
///
/// <para><b>Não existe coluna de imputação aqui, e isso é proposital</b>: expor <c>IsImputed</c> ao modelo lhe
/// daria a chave para isolar o artefato da imputação e acertar pelo motivo errado. A flag existe no domínio
/// para RELATAR a composição dos conjuntos, não para o modelo consumir.</para>
///
/// <para><b>E3.3 — codificação por tipo.</b> <see cref="Key"/> e <see cref="TimeSignature"/> são CATEGÓRICAS
/// (não ordinais): além do valor numérico bruto — mantido para rastro — viajam como texto em
/// <see cref="KeyCategory"/>/<see cref="TimeSignatureCategory"/>, que é o que o pipeline transforma em one-hot.
/// <see cref="Mode"/> é binário e entra direto como 0/1. <see cref="GenreCategory"/> normaliza gênero
/// ausente/vazio para um bucket sentinela conhecido, para a inferência com gênero desconhecido nunca quebrar.</para>
/// </summary>
internal sealed class PopularityTrainingRow
{
    /// <summary>Bucket sentinela para gênero ausente/desconhecido — one-hot próprio, nunca confundido com um gênero real.</summary>
    internal const string UnknownGenre = "<unknown>";

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

    /// <summary>Tonalidade (0–11), mantida como número para rastro; a feature é <see cref="KeyCategory"/>.</summary>
    public float Key { get; set; }

    /// <summary>Modo (0 = menor, 1 = maior). Binário, entra direto como 0/1 no vetor — sem one-hot.</summary>
    public float Mode { get; set; }

    /// <summary>Compasso, mantido como número para rastro; a feature é <see cref="TimeSignatureCategory"/>.</summary>
    public float TimeSignature { get; set; }

    /// <summary><c>Key</c> como texto — insumo do one-hot do Bloco A.</summary>
    public string KeyCategory { get; set; } = string.Empty;

    /// <summary><c>TimeSignature</c> como texto — insumo do one-hot do Bloco A.</summary>
    public string TimeSignatureCategory { get; set; } = string.Empty;

    /// <summary>Gênero cru, mantido para rastro. A feature codificada é <see cref="GenreCategory"/>.</summary>
    public string Genre { get; set; } = string.Empty;

    /// <summary>Gênero normalizado (bucket sentinela quando ausente) — insumo do one-hot do Bloco B.</summary>
    public string GenreCategory { get; set; } = string.Empty;

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
            sample.Explicit,
            sample.Key,
            sample.Mode,
            sample.TimeSignature,
            sample.Genre);

        row.TrackId = sample.TrackId;
        row.Popularity = sample.Popularity;

        return row;
    }

    /// <summary>
    /// Monta a linha do ML.NET a partir das features que o pipeline pode consumir. É o ÚNICO caminho de
    /// tradução das features, usado tanto pelo treino quanto pela inferência: a conversão <see cref="double"/> →
    /// <see cref="float"/>, a codificação de <c>explicit</c> como 0/1 e a normalização de gênero ausente para o
    /// bucket sentinela acontecem aqui, e em lugar nenhum além daqui — é isso que impede skew treino/inferência.
    ///
    /// <para><see cref="Key"/>, <see cref="Mode"/>, <see cref="TimeSignature"/> e <c>genre</c> do Bloco A/B
    /// (E3.3) entram sempre na linha; se o feature set campeão não os usar, as colunas one-hot simplesmente não
    /// são concatenadas pelo pipeline. Manter a assinatura estável impede que um caminho de inferência esqueça
    /// de preenchê-los.</para>
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
        bool @explicit,
        int key,
        int mode,
        int timeSignature,
        string? genre) => new()
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
        Loudness = (float)loudness,
        Key = key,
        Mode = mode,
        TimeSignature = timeSignature,
        KeyCategory = key.ToString(CultureInfo.InvariantCulture),
        TimeSignatureCategory = timeSignature.ToString(CultureInfo.InvariantCulture),
        Genre = genre ?? string.Empty,
        GenreCategory = NormalizeGenre(genre)
    };

    /// <summary>
    /// Gênero ausente/vazio vira o bucket <see cref="UnknownGenre"/>: assim o one-hot lhe dá uma coluna própria
    /// e conhecida, em vez de um vetor todo-zero que o modelo interpretaria como "nenhuma categoria" — dois
    /// estados diferentes que não devem colapsar.
    /// </summary>
    private static string NormalizeGenre(string? genre) =>
        string.IsNullOrWhiteSpace(genre) ? UnknownGenre : genre;
}
