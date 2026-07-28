namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Imputation;

/// <summary>
/// Valores completos de uma linha após o tratamento de faltantes, com a marca de <b>procedência</b>:
/// <see cref="IsImputed"/> indica que pelo menos um valor foi inferido, e <see cref="ImputedFeatures"/> diz
/// exatamente quais. Nunca preencher em silêncio é a regra do projeto — quem treina o modelo (E3) precisa
/// poder excluir ou ponderar o que foi inventado.
/// </summary>
public sealed record ImputedAudioFeatures(
    IReadOnlyDictionary<AudioFeature, double> Values,
    IReadOnlyCollection<AudioFeature> ImputedFeatures)
{
    public bool IsImputed => ImputedFeatures.Count > 0;

    public double this[AudioFeature feature] => Values[feature];
}

/// <summary>
/// Preenche os atributos ausentes de uma linha do dataset. Porta explícita porque a política de imputação é
/// uma decisão que muda (mediana por gênero hoje; kNN ou exclusão do treino amanhã) e não deve ficar
/// entranhada no handler de importação.
/// </summary>
public interface IAudioFeatureImputer
{
    ImputedAudioFeatures Impute(KaggleAudioFeaturesRow row, AudioFeatureMedianProfile profile);
}

/// <summary>
/// Política padrão (RF3): valor ausente vira a <b>mediana do gênero</b> da faixa, com a mediana global como
/// retaguarda. Features discretas (tonalidade, modo, compasso) são arredondadas — não existe compasso 3,5.
///
/// Quando nem a mediana global existe (a feature está ausente no dataset inteiro) o valor cai para zero e
/// segue marcado como imputado: é o único desfecho honesto, já que o value object exige um número e não há
/// absolutamente nada de onde inferir.
/// </summary>
public sealed class MedianAudioFeatureImputer : IAudioFeatureImputer
{
    public ImputedAudioFeatures Impute(KaggleAudioFeaturesRow row, AudioFeatureMedianProfile profile)
    {
        var values = new Dictionary<AudioFeature, double>(AudioFeatureAccessor.All.Count);
        var imputed = new List<AudioFeature>();

        foreach (AudioFeature feature in AudioFeatureAccessor.All)
        {
            double? observed = AudioFeatureAccessor.ValueOf(row, feature);

            if (observed is not null)
            {
                values[feature] = observed.Value;
                continue;
            }

            double replacement = profile.MedianFor(feature, row.Genre) ?? 0d;

            values[feature] = AudioFeatureAccessor.IsDiscrete(feature)
                ? Math.Round(replacement, MidpointRounding.AwayFromZero)
                : replacement;

            imputed.Add(feature);
        }

        return new ImputedAudioFeatures(values, imputed);
    }
}
