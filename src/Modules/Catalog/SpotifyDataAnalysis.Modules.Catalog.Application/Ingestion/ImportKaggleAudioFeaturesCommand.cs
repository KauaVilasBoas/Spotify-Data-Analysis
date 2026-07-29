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
/// Métrica de qualidade da importação — o relatório que diz o quanto confiar no dado que acabou de entrar.
/// Discrimina <b>como</b> cada linha foi casada, quantas linhas eram duplicadas (o dataset repete a mesma
/// faixa uma vez por gênero; a primeira ocorrência vence) e quantas faixas ficaram com pelo menos um atributo
/// <b>imputado</b> em vez de medido.
///
/// A distribuição entre os modos de casamento é o indicador central: <see cref="MatchedById"/> é o caminho
/// exato; <see cref="MatchedByNameAndDuration"/> é a chave textual <b>confirmada pela duração</b> (E1.9), que
/// desambigua homônimos; <see cref="MatchedByNameAndArtist"/> é o fallback textual puro — o único que a
/// <see cref="FallbackRate"/> conta, porque é o único casamento sem confirmação, sujeito a colisão.
/// </summary>
public sealed record ImportKaggleAudioFeaturesResult(
    int MatchedById,
    int MatchedByNameAndDuration,
    int MatchedByNameAndArtist,
    int Unmatched,
    int Duplicates,
    int Imputed,
    int Total)
{
    /// <summary>Linhas que casaram com alguma faixa do catálogo, por qualquer estratégia.</summary>
    public int Matched => MatchedById + MatchedByNameAndDuration + MatchedByNameAndArtist;

    /// <summary>Fração de linhas do CSV que casaram com uma faixa do catálogo (0..1).</summary>
    public double MatchRate => Total == 0 ? 0d : (double)Matched / Total;

    /// <summary>
    /// Fração dos casamentos obtida pelo <b>fallback textual puro</b> (0..1) — só o nome+artista, sem
    /// confirmação por id nem por duração. Quanto maior, mais o resultado depende de heurística sujeita a
    /// colisão de homônimos; é o indicador a observar antes de confiar nas features importadas.
    ///
    /// O casamento por nome+duração <b>não</b> entra aqui de propósito: a duração confirma a faixa, então não
    /// é o mesmo risco que a chave textual solta — contá-lo como fallback esconderia justamente o ganho de
    /// qualidade que a estratégia (E1.9) traz.
    /// </summary>
    public double FallbackRate => Matched == 0 ? 0d : (double)MatchedByNameAndArtist / Matched;

    /// <summary>Fração das faixas casadas cujas features tiveram algum valor inferido (0..1).</summary>
    public double ImputationRate => Matched == 0 ? 0d : (double)Imputed / Matched;
}
