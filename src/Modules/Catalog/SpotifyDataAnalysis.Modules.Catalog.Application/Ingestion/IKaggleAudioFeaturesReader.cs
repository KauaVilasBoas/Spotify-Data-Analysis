namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Porta de leitura do CSV de audio-features do Kaggle. Percorre as linhas preguiçosamente (uma por vez),
/// sem carregar o arquivo inteiro em memória. A implementação (CsvHelper) fica na Infrastructure.
/// </summary>
public interface IKaggleAudioFeaturesReader
{
    IAsyncEnumerable<KaggleAudioFeaturesRow> ReadAsync(
        string csvFilePath, CancellationToken cancellationToken = default);
}
