using System.Data;
using System.Text.Json;
using Dapper;
using SpotifyDataAnalysis.Infrastructure.Data;
using SpotifyDataAnalysis.SharedKernel.Exceptions;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Tracks;

/// <summary>
/// Detalhe de uma faixa: dados de catálogo somados às audio-features, quando a faixa já foi casada com o
/// dataset externo. Faixa inexistente resulta em <see cref="NotFoundException"/>.
/// </summary>
public sealed record GetTrackByIdQuery(string TrackId) : IQuery<TrackDetailResult>;

/// <summary>Crédito de artista da faixa, na ordem devolvida pela API — o primeiro é o principal.</summary>
public sealed record TrackArtistItem(string Id, string Name);

/// <summary>
/// As audio-features da faixa. <paramref name="IsImputed"/> e <paramref name="Source"/> são explícitos porque
/// parte dos valores é preenchida por imputação, e tratar imputado como medido leva a conclusão errada.
/// </summary>
public sealed record TrackAudioFeaturesDetail(
    double Danceability,
    double Energy,
    double Valence,
    double Tempo,
    double Acousticness,
    double Instrumentalness,
    double Liveness,
    double Speechiness,
    double Loudness,
    int Key,
    int Mode,
    int TimeSignature,
    string? Genre,
    string Source,
    bool IsImputed);

/// <summary>
/// O detalhe completo da faixa. <paramref name="AudioFeatures"/> é nulo quando a faixa ainda não foi casada com
/// o dataset — estado legítimo do catálogo, não erro.
/// </summary>
public sealed record TrackDetailResult(
    string TrackId,
    string Name,
    int Popularity,
    int DurationMs,
    bool Explicit,
    string? Isrc,
    string? AlbumId,
    IReadOnlyList<TrackArtistItem> Artists,
    TrackAudioFeaturesDetail? AudioFeatures);

internal sealed class GetTrackByIdQueryHandler
    : BaseDataAccess, IQueryHandler<GetTrackByIdQuery, TrackDetailResult>
{
    internal const string Sql =
        """
        SELECT
            t.id                    AS "TrackId",
            t.name                  AS "Name",
            t.popularity            AS "Popularity",
            t.duration_ms           AS "DurationMs",
            t.explicit              AS "Explicit",
            t.isrc                  AS "Isrc",
            t.album_id              AS "AlbumId",
            t.artists::text         AS "ArtistsJson",
            t.audio_features::text  AS "AudioFeaturesJson"
        FROM catalog.tracks AS t
        WHERE t.id = @TrackId;
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GetTrackByIdQueryHandler(DbConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public async Task<TrackDetailResult> HandleAsync(
        GetTrackByIdQuery request, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = await OpenConnectionAsync();

        var command = new CommandDefinition(
            Sql, new { request.TrackId }, cancellationToken: cancellationToken);

        TrackDetailRow? row = await connection.QuerySingleOrDefaultAsync<TrackDetailRow>(command);

        if (row is null)
            throw new NotFoundException("Track", request.TrackId);

        return new TrackDetailResult(
            row.TrackId,
            row.Name,
            row.Popularity,
            row.DurationMs,
            row.Explicit,
            row.Isrc,
            row.AlbumId,
            Deserialize<List<TrackArtistItem>>(row.ArtistsJson) ?? [],
            Deserialize<TrackAudioFeaturesDetail>(row.AudioFeaturesJson));
    }

    private static T? Deserialize<T>(string? json)
        => string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);

    private sealed record TrackDetailRow(
        string TrackId,
        string Name,
        int Popularity,
        int DurationMs,
        bool Explicit,
        string? Isrc,
        string? AlbumId,
        string? ArtistsJson,
        string? AudioFeaturesJson);
}
