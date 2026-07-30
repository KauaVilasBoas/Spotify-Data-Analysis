namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>De que lado do split uma amostra caiu.</summary>
public enum DatasetPartition
{
    /// <summary>Conjunto de treino.</summary>
    Training = 0,

    /// <summary>Conjunto de teste (holdout), nunca visto pelo treino.</summary>
    Test = 1
}
