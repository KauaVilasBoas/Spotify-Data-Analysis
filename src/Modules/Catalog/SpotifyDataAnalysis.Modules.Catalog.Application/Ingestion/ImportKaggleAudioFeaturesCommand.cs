using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Importa os audio-features do CSV do Kaggle para o catálogo, casando cada linha com uma faixa já coletada
/// pela API. É um command (muda estado) — SaveChanges + Outbox no commit do TransactionBehavior/UnitOfWork.
///
/// A API do Spotify deixou de expor audio-features (nov/2024), então este é o único caminho para alimentar
/// a EDA (E2) e o modelo de ML (E3).
/// </summary>
public sealed record ImportKaggleAudioFeaturesCommand(string CsvFilePath)
    : ICommand<ImportKaggleAudioFeaturesResult>;

/// <summary>
/// Métrica de qualidade da importação. Além do total, discrimina <b>como</b> cada linha foi casada — a
/// proporção entre <see cref="MatchedById"/> e <see cref="MatchedByNameAndArtist"/> diz o quanto o resultado
/// depende de heurística textual — e quantas linhas eram duplicadas (o dataset repete a mesma faixa uma vez
/// por gênero; a primeira ocorrência vence).
/// </summary>
public sealed record ImportKaggleAudioFeaturesResult(
    int MatchedById,
    int MatchedByNameAndArtist,
    int Unmatched,
    int Duplicates,
    int Total)
{
    /// <summary>Linhas que casaram com alguma faixa do catálogo, por qualquer estratégia.</summary>
    public int Matched => MatchedById + MatchedByNameAndArtist;

    /// <summary>Fração de linhas do CSV que casaram com uma faixa do catálogo (0..1).</summary>
    public double MatchRate => Total == 0 ? 0d : (double)Matched / Total;

    /// <summary>
    /// Fração dos casamentos obtida pelo fallback textual (0..1). Quanto maior, mais o resultado depende de
    /// heurística — o indicador a observar antes de confiar nas features importadas.
    /// </summary>
    public double FallbackRate => Matched == 0 ? 0d : (double)MatchedByNameAndArtist / Matched;
}
