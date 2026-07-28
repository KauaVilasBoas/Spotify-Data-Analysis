using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Matching;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Handler de <see cref="ImportKaggleAudioFeaturesCommand"/>: percorre o CSV em streaming, casa cada linha
/// com uma faixa do catálogo pelo <see cref="TrackMatcher"/> (id primeiro, nome+artista como fallback) e
/// anexa os audio-features ao agregado, contabilizando a métrica de match.
///
/// O handler NÃO conhece as estratégias de casamento nem lê o arquivo: ambas as responsabilidades ficam
/// atrás de colaboradores (<see cref="IKaggleAudioFeaturesReader"/>, <see cref="TrackMatcher"/>), o que o
/// mantém como um orquestrador testável sem disco e permite acrescentar novas formas de casar sem tocá-lo.
/// </summary>
internal sealed class ImportKaggleAudioFeaturesCommandHandler
    : ICommandHandler<ImportKaggleAudioFeaturesCommand, ImportKaggleAudioFeaturesResult>
{
    /// <summary>Origem gravada nas features importadas — rastreia de onde o dado veio (auditoria da ingestão).</summary>
    private const string SourceName = "kaggle:spotify-tracks-dataset";

    private readonly IKaggleAudioFeaturesReader _reader;
    private readonly TrackMatcher _matcher;

    public ImportKaggleAudioFeaturesCommandHandler(IKaggleAudioFeaturesReader reader, TrackMatcher matcher)
    {
        _reader = reader;
        _matcher = matcher;
    }

    public async Task<ImportKaggleAudioFeaturesResult> HandleAsync(
        ImportKaggleAudioFeaturesCommand request, CancellationToken cancellationToken = default)
    {
        // Dedupe pela FAIXA CASADA (e não pela linha do CSV): o dataset lista a mesma faixa uma vez por
        // gênero, e sem isso um gênero arbitrário — o último lido — sobrescreveria os anteriores. Casar antes
        // de deduplicar também cobre o caso de duas linhas com ids diferentes que caem na mesma faixa via
        // fallback textual.
        var attachedTrackIds = new HashSet<string>(StringComparer.Ordinal);

        int matchedById = 0, matchedByNameAndArtist = 0, unmatched = 0, duplicates = 0, total = 0;

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

            track.AttachAudioFeatures(BuildFeatures(row));

            if (match.Kind == TrackMatchKind.SpotifyTrackId)
                matchedById++;
            else
                matchedByNameAndArtist++;
        }

        return new ImportKaggleAudioFeaturesResult(
            matchedById, matchedByNameAndArtist, unmatched, duplicates, total);
    }

    private static AudioFeatures BuildFeatures(KaggleAudioFeaturesRow row)
        => AudioFeatures.Create(
            danceability: row.Danceability,
            energy: row.Energy,
            valence: row.Valence,
            tempo: row.Tempo,
            acousticness: row.Acousticness,
            instrumentalness: row.Instrumentalness,
            liveness: row.Liveness,
            speechiness: row.Speechiness,
            loudness: row.Loudness,
            key: row.Key,
            mode: row.Mode,
            timeSignature: row.TimeSignature,
            source: SourceName,
            genre: row.Genre);
}
