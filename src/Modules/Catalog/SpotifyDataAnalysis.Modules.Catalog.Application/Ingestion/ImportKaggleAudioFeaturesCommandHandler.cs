using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Imputation;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Matching;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Handler de <see cref="ImportKaggleAudioFeaturesCommand"/>. Orquestra a importação em duas passadas sobre
/// o CSV:
///
/// <list type="number">
///   <item><b>perfilagem</b> — observa as linhas e calcula as medianas por gênero (E1.5);</item>
///   <item><b>importação</b> — casa cada linha com o catálogo pelo <see cref="TrackMatcher"/> (id primeiro,
///         nome+artista como fallback), preenche os faltantes com o <see cref="IAudioFeatureImputer"/> e
///         anexa os atributos ao agregado.</item>
/// </list>
///
/// <b>Por que duas passadas:</b> a mediana só existe depois de ver a distribuição inteira. Reler um arquivo
/// local é barato perto de manter ~114k linhas materializadas em memória, e mantém o fluxo linear e óbvio.
///
/// O handler não lê arquivo, não sabe casar e não decide política de imputação — as três responsabilidades
/// estão atrás de colaboradores, o que o deixa testável sem disco e aberto a novas estratégias sem edição.
/// </summary>
internal sealed class ImportKaggleAudioFeaturesCommandHandler
    : ICommandHandler<ImportKaggleAudioFeaturesCommand, ImportKaggleAudioFeaturesResult>
{
    /// <summary>Origem gravada nas features importadas — rastreia de onde o dado veio (auditoria da ingestão).</summary>
    private const string SourceName = "kaggle:spotify-tracks-dataset";

    private readonly IKaggleAudioFeaturesReader _reader;
    private readonly TrackMatcher _matcher;
    private readonly IAudioFeatureImputer _imputer;

    public ImportKaggleAudioFeaturesCommandHandler(
        IKaggleAudioFeaturesReader reader, TrackMatcher matcher, IAudioFeatureImputer imputer)
    {
        _reader = reader;
        _matcher = matcher;
        _imputer = imputer;
    }

    public async Task<ImportKaggleAudioFeaturesResult> HandleAsync(
        ImportKaggleAudioFeaturesCommand request, CancellationToken cancellationToken = default)
    {
        AudioFeatureMedianProfile medians = await BuildMedianProfileAsync(request.CsvFilePath, cancellationToken);

        // Dedupe pela FAIXA CASADA (e não pela linha do CSV): o dataset lista a mesma faixa uma vez por
        // gênero, e sem isso um gênero arbitrário — o último lido — sobrescreveria os anteriores. Casar antes
        // de deduplicar também cobre duas linhas de ids diferentes que caem na mesma faixa via fallback.
        var attachedTrackIds = new HashSet<string>(StringComparer.Ordinal);

        // A contagem é dirigida pelo TrackMatchKind da estratégia vencedora (não por um if/else por modo):
        // acrescentar uma estratégia à chain passa a discriminar sua métrica sem tocar neste laço — só o
        // Result precisa expor o novo contador.
        var matchedByKind = new Dictionary<TrackMatchKind, int>();

        int unmatched = 0, duplicates = 0, imputed = 0, total = 0;

        await foreach (KaggleAudioFeaturesRow row in _reader.ReadAsync(request.CsvFilePath, cancellationToken))
        {
            total++;

            TrackMatch match = await _matcher.MatchAsync(row, cancellationToken);

            if (!match.IsMatch)
            {
                unmatched++;
                continue;
            }

            Track track = match.Track!;

            if (!attachedTrackIds.Add(track.Id.Value))
            {
                duplicates++;
                continue;
            }

            ImputedAudioFeatures values = _imputer.Impute(row, medians);
            track.AttachAudioFeatures(BuildFeatures(values, row.Genre));

            if (values.IsImputed)
                imputed++;

            matchedByKind[match.Kind] = matchedByKind.GetValueOrDefault(match.Kind) + 1;
        }

        return new ImportKaggleAudioFeaturesResult(
            MatchedById: matchedByKind.GetValueOrDefault(TrackMatchKind.SpotifyTrackId),
            MatchedByNameAndDuration: matchedByKind.GetValueOrDefault(TrackMatchKind.NameAndDuration),
            MatchedByNameAndArtist: matchedByKind.GetValueOrDefault(TrackMatchKind.NameAndArtist),
            Unmatched: unmatched, Duplicates: duplicates, Imputed: imputed, Total: total);
    }

    private async Task<AudioFeatureMedianProfile> BuildMedianProfileAsync(
        string csvFilePath, CancellationToken cancellationToken)
    {
        var builder = new AudioFeatureMedianProfileBuilder();

        await foreach (KaggleAudioFeaturesRow row in _reader.ReadAsync(csvFilePath, cancellationToken))
            builder.Observe(row);

        return builder.Build();
    }

    private static AudioFeatures BuildFeatures(ImputedAudioFeatures values, string? genre)
        => AudioFeatures.Create(
            danceability: values[AudioFeature.Danceability],
            energy: values[AudioFeature.Energy],
            valence: values[AudioFeature.Valence],
            tempo: values[AudioFeature.Tempo],
            acousticness: values[AudioFeature.Acousticness],
            instrumentalness: values[AudioFeature.Instrumentalness],
            liveness: values[AudioFeature.Liveness],
            speechiness: values[AudioFeature.Speechiness],
            loudness: values[AudioFeature.Loudness],
            key: (int)values[AudioFeature.Key],
            mode: (int)values[AudioFeature.Mode],
            timeSignature: (int)values[AudioFeature.TimeSignature],
            source: SourceName,
            genre: genre,
            isImputed: values.IsImputed);
}
