namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>
/// Uma faixa do catálogo <b>antes</b> de qualquer julgamento de elegibilidade: o estado cru, com todos os
/// campos opcionais, exatamente como o catálogo pode entregá-lo. Faixa sem audio-features, faixa cujo jsonb
/// não traz alguma chave e faixa sem gênero são estados legítimos e comuns — por isso nada aqui é obrigatório
/// além da identidade.
///
/// <para>É o insumo de <see cref="TrainingEligibilitySpecification"/>. Só um candidato aprovado vira
/// <see cref="TrackTrainingSample"/>, que já é um registro completo e sem nulos.</para>
/// </summary>
public sealed record TrackTrainingCandidate
{
    /// <summary>Identidade da faixa no Spotify, guardada por valor (o Prediction não conhece tipos do Catalog).</summary>
    public required string TrackId { get; init; }

    /// <summary>Alvo do modelo (0–100). Nulo quando o catálogo não conhece a popularidade da faixa.</summary>
    public int? Popularity { get; init; }

    /// <summary>Se a faixa tem audio-features associadas (coluna jsonb não nula).</summary>
    public required bool HasAudioFeatures { get; init; }

    /// <summary>Se os valores das audio-features foram preenchidos por imputação em vez de medidos.</summary>
    public required bool IsImputed { get; init; }

    public double? Danceability { get; init; }
    public double? Energy { get; init; }
    public double? Valence { get; init; }
    public double? Tempo { get; init; }
    public double? Acousticness { get; init; }
    public double? Instrumentalness { get; init; }
    public double? Liveness { get; init; }
    public double? Speechiness { get; init; }
    public double? Loudness { get; init; }

    public int? Key { get; init; }
    public int? Mode { get; init; }
    public int? TimeSignature { get; init; }

    /// <summary>Gênero declarado pelo dataset de origem. Opcional: ausência de gênero não desqualifica a faixa.</summary>
    public string? Genre { get; init; }

    /// <summary>
    /// Se as doze grandezas numéricas exigidas pelo dataset de treino estão presentes. As nove contínuas
    /// alimentam o modelo desde o E3.2; <see cref="Key"/>, <see cref="Mode"/> e <see cref="TimeSignature"/>
    /// são exigidas junto porque entram como features codificadas no E3.3 — aceitar hoje uma faixa sem elas
    /// criaria um dataset que muda de tamanho no meio do épico.
    /// </summary>
    public bool HasCompleteAudioFeatures =>
        Danceability.HasValue
        && Energy.HasValue
        && Valence.HasValue
        && Tempo.HasValue
        && Acousticness.HasValue
        && Instrumentalness.HasValue
        && Liveness.HasValue
        && Speechiness.HasValue
        && Loudness.HasValue
        && Key.HasValue
        && Mode.HasValue
        && TimeSignature.HasValue;
}
