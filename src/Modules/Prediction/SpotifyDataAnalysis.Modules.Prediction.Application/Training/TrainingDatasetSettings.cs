namespace SpotifyDataAnalysis.Modules.Prediction.Application.Training;

/// <summary>
/// Configuração do dataset de treino, ligada à seção <c>Prediction:TrainingDataset</c>. A Application declara
/// o contrato de configuração de que precisa; quem faz o binding é o composition root do módulo.
///
/// <para>A semente vive aqui, e não no código, porque é ela que torna um treino <b>reproduzível</b>: mudar a
/// semente é uma decisão consciente de configuração, não um efeito colateral de deploy.</para>
/// </summary>
public sealed class TrainingDatasetSettings
{
    /// <summary>Caminho da seção de configuração.</summary>
    public const string SectionName = "Prediction:TrainingDataset";

    /// <summary>Semente padrão do split treino/teste.</summary>
    public int Seed { get; set; } = 20260730;

    /// <summary>Fração padrão do conjunto elegível reservada para teste.</summary>
    public double TestFraction { get; set; } = 0.2;

    /// <summary>
    /// Tamanho do lote de leitura do catálogo. A leitura é paginada por keyset para que o pico de memória do
    /// transporte não escale com o tamanho do catálogo — restrição de um host de free tier (256–512 MB), onde
    /// puxar as ~114k faixas num único result set é o caminho mais curto para um OOM.
    /// </summary>
    public int ReadBatchSize { get; set; } = 5_000;
}
