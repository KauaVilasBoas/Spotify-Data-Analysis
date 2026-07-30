using System.Text.Json;
using SpotifyDataAnalysis.Modules.Catalog.Application.Tracks;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Application;

/// <summary>
/// O detalhe da faixa desserializa as colunas jsonb nos records do contrato, então uma divergência de nome
/// entre o value object do write-side e o record de leitura zeraria campos em silêncio. Estes testes partem do
/// objeto de domínio real e exigem que cada campo chegue ao contrato.
/// </summary>
public sealed class TrackDetailJsonContractTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void AudioFeaturesDetail_DeserializesEveryFieldFromTheDomainValueObjectShape()
    {
        AudioFeatures domain = AudioFeatures.Create(
            danceability: 0.81, energy: 0.62, valence: 0.44, tempo: 128.5,
            acousticness: 0.11, instrumentalness: 0.02, liveness: 0.09, speechiness: 0.05,
            loudness: -6.4, key: 7, mode: 1, timeSignature: 4,
            source: "kaggle-spotify-tracks", genre: "Electronic", isImputed: true);

        string json = JsonSerializer.Serialize(domain);

        TrackAudioFeaturesDetail? detail =
            JsonSerializer.Deserialize<TrackAudioFeaturesDetail>(json, Options);

        Assert.NotNull(detail);
        Assert.Equal(0.81, detail.Danceability);
        Assert.Equal(0.62, detail.Energy);
        Assert.Equal(0.44, detail.Valence);
        Assert.Equal(128.5, detail.Tempo);
        Assert.Equal(0.11, detail.Acousticness);
        Assert.Equal(0.02, detail.Instrumentalness);
        Assert.Equal(0.09, detail.Liveness);
        Assert.Equal(0.05, detail.Speechiness);
        Assert.Equal(-6.4, detail.Loudness);
        Assert.Equal(7, detail.Key);
        Assert.Equal(1, detail.Mode);
        Assert.Equal(4, detail.TimeSignature);
        Assert.Equal("kaggle-spotify-tracks", detail.Source);
        Assert.Equal("electronic", detail.Genre);
        Assert.True(detail.IsImputed);
    }

    [Fact]
    public void AudioFeaturesDetail_CarriesTheMeasuredFlag_WhenNothingWasImputed()
    {
        AudioFeatures measured = AudioFeatures.Create(
            danceability: 0.5, energy: 0.5, valence: 0.5, tempo: 100,
            acousticness: 0.5, instrumentalness: 0.5, liveness: 0.5, speechiness: 0.5,
            loudness: -5, key: 0, mode: 0, timeSignature: 4,
            source: "kaggle-spotify-tracks");

        TrackAudioFeaturesDetail? detail = JsonSerializer.Deserialize<TrackAudioFeaturesDetail>(
            JsonSerializer.Serialize(measured), Options);

        Assert.NotNull(detail);
        Assert.False(detail.IsImputed);
        Assert.Null(detail.Genre);
    }

    [Fact]
    public void ArtistItems_DeserializeFromTheDomainCreditShape_PreservingApiOrder()
    {
        List<TrackArtist> credits =
        [
            TrackArtist.Of("artist-1", "Daft Punk"),
            TrackArtist.Of("artist-2", "Pharrell Williams")
        ];

        string json = JsonSerializer.Serialize(credits);

        List<TrackArtistItem>? items = JsonSerializer.Deserialize<List<TrackArtistItem>>(json, Options);

        Assert.NotNull(items);
        Assert.Collection(
            items,
            first =>
            {
                Assert.Equal("artist-1", first.Id);
                Assert.Equal("Daft Punk", first.Name);
            },
            second =>
            {
                Assert.Equal("artist-2", second.Id);
                Assert.Equal("Pharrell Williams", second.Name);
            });
    }
}
