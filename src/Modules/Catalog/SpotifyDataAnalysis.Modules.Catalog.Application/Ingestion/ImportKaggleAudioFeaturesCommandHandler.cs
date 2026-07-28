using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Handler de <see cref="ImportKaggleAudioFeaturesCommand"/>: lê o CSV (streaming), casa cada linha por
/// <c>track_id</c> com uma faixa do catálogo e anexa os <see cref="AudioFeatures"/>. Linhas sem
/// correspondência entram na contagem de não-casadas (métrica de qualidade da importação — RF3/DP-2).
///
/// O <i>fallback</i> por nome+artista (quando o id não bate) fica para uma fatia seguinte — ele exige uma
/// consulta de leitura por nome+artista que ainda não existe no repositório.
/// </summary>
internal sealed class ImportKaggleAudioFeaturesCommandHandler
    : ICommandHandler<ImportKaggleAudioFeaturesCommand, ImportKaggleAudioFeaturesResult>
{
    private const string Source = "kaggle:spotify-tracks-dataset";

    private readonly IKaggleAudioFeaturesReader _reader;
    private readonly ITrackRepository _tracks;

    public ImportKaggleAudioFeaturesCommandHandler(IKaggleAudioFeaturesReader reader, ITrackRepository tracks)
    {
        _reader = reader;
        _tracks = tracks;
    }

    public async Task<ImportKaggleAudioFeaturesResult> HandleAsync(
        ImportKaggleAudioFeaturesCommand request, CancellationToken cancellationToken = default)
    {
        int matched = 0, unmatched = 0, total = 0;

        await foreach (KaggleAudioFeaturesRow row in _reader.ReadAsync(request.CsvFilePath, cancellationToken))
        {
            total++;

            if (string.IsNullOrWhiteSpace(row.TrackId))
            {
                unmatched++;
                continue;
            }

            Track? track = await _tracks.GetByIdAsync(SpotifyTrackId.Of(row.TrackId), cancellationToken);
            if (track is null)
            {
                unmatched++;
                continue;
            }

            track.AttachAudioFeatures(AudioFeatures.Create(
                row.Danceability, row.Energy, row.Valence, row.Tempo, row.Acousticness,
                row.Instrumentalness, row.Liveness, row.Speechiness, row.Loudness,
                row.Key, row.Mode, row.TimeSignature, source: Source));

            matched++;
        }

        return new ImportKaggleAudioFeaturesResult(matched, unmatched, total);
    }
}
