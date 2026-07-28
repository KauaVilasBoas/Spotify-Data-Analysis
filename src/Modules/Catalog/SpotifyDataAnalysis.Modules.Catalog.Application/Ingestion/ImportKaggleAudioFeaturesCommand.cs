using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Importa os audio-features do CSV do Kaggle, casando por <c>track_id</c> com as faixas já no catálogo e
/// anexando-os (tratamento de faltantes/imputação vem no E1.5). É um command — o SaveChanges é disparado
/// pelo TransactionBehavior/UnitOfWork do módulo.
/// </summary>
public sealed record ImportKaggleAudioFeaturesCommand(string CsvFilePath)
    : ICommand<ImportKaggleAudioFeaturesResult>;

/// <summary>
/// Métrica da importação: quantas linhas casaram com o catálogo, quantas não casaram, quantas eram
/// <b>duplicadas</b> (o dataset Kaggle repete a mesma faixa em vários gêneros — a primeira ocorrência vence)
/// e o total lido, além da taxa de match.
/// </summary>
public sealed record ImportKaggleAudioFeaturesResult(int Matched, int Unmatched, int Duplicates, int Total)
{
    /// <summary>Fração de linhas do CSV que casaram com uma faixa do catálogo (0..1).</summary>
    public double MatchRate => Total == 0 ? 0d : (double)Matched / Total;
}
