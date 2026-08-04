using SpotifyDataAnalysis.SharedKernel.Exceptions;
using SpotifyDataAnalysis.SharedKernel.Guards;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>
/// Uma linha do dataset de treino: uma faixa já <b>aprovada</b> pela regra de elegibilidade, portanto sem
/// nulos nas grandezas do modelo. É o contrato interno que o E3.2 consome — o tipo de linha tipado do qual o
/// <c>IDataView</c> do ML.NET é materializado, sem CSV nem <c>dynamic</c> no meio.
///
/// <para><see cref="IsImputed"/> viaja junto para efeito de <b>relato</b> (saber o que compõe cada conjunto),
/// e não como feature: dar ao modelo a flag de imputação é dar a ele a chave para isolar o artefato da
/// imputação e acertar pelo motivo errado.</para>
/// </summary>
public sealed record TrackTrainingSample
{
    private TrackTrainingSample()
    {
    }

    /// <summary>Identidade da faixa, guardada por valor. Não é feature: serve de chave do split e de rastro.</summary>
    public required string TrackId { get; init; }

    /// <summary>Alvo da regressão (0–100).</summary>
    public required int Popularity { get; init; }

    /// <summary>Duração em milissegundos. Feature do modelo desde o E3.2.</summary>
    public required int DurationMs { get; init; }

    /// <summary>Se a faixa é explícita. Feature booleana do modelo desde o E3.2.</summary>
    public required bool Explicit { get; init; }

    public required double Danceability { get; init; }
    public required double Energy { get; init; }
    public required double Valence { get; init; }
    public required double Tempo { get; init; }
    public required double Acousticness { get; init; }
    public required double Instrumentalness { get; init; }
    public required double Liveness { get; init; }
    public required double Speechiness { get; init; }
    public required double Loudness { get; init; }

    public required int Key { get; init; }
    public required int Mode { get; init; }
    public required int TimeSignature { get; init; }

    /// <summary>Gênero declarado, quando houver. Feature categórica a partir do E3.3.</summary>
    public string? Genre { get; init; }

    /// <summary>Se as features desta linha foram imputadas. Metadado de relato, nunca feature.</summary>
    public required bool IsImputed { get; init; }

    /// <summary>
    /// Promove um candidato APROVADO a linha do dataset. Falha alto se o candidato ainda não passou pela
    /// <see cref="TrainingEligibilitySpecification"/>: a única forma de construir uma amostra é ter provado
    /// antes que ela é elegível, o que impede um nulo de virar zero silenciosamente no meio do dataset.
    /// </summary>
    /// <exception cref="DomainException">Quando o candidato não é estruturalmente elegível.</exception>
    public static TrackTrainingSample FromEligibleCandidate(TrackTrainingCandidate candidate)
    {
        Guard.AgainstNull(candidate, nameof(candidate));
        Guard.AgainstNullOrWhiteSpace(candidate.TrackId, nameof(candidate.TrackId));

        if (candidate.Popularity is not { } popularity)
            throw new DomainException("Uma faixa sem popularidade não pode virar linha do dataset de treino.");

        if (!candidate.HasAudioFeatures || !candidate.HasCompleteAudioFeatures)
            throw new DomainException(
                "Uma faixa sem audio-features completas não pode virar linha do dataset de treino.");

        return new TrackTrainingSample
        {
            TrackId = candidate.TrackId,
            Popularity = popularity,
            DurationMs = candidate.DurationMs,
            Explicit = candidate.Explicit,
            Danceability = candidate.Danceability!.Value,
            Energy = candidate.Energy!.Value,
            Valence = candidate.Valence!.Value,
            Tempo = candidate.Tempo!.Value,
            Acousticness = candidate.Acousticness!.Value,
            Instrumentalness = candidate.Instrumentalness!.Value,
            Liveness = candidate.Liveness!.Value,
            Speechiness = candidate.Speechiness!.Value,
            Loudness = candidate.Loudness!.Value,
            Key = candidate.Key!.Value,
            Mode = candidate.Mode!.Value,
            TimeSignature = candidate.TimeSignature!.Value,
            Genre = candidate.Genre,
            IsImputed = candidate.IsImputed
        };
    }
}
