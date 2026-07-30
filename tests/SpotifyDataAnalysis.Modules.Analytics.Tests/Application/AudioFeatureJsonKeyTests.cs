using SpotifyDataAnalysis.Modules.Analytics.Application.Insights;

namespace SpotifyDataAnalysis.Modules.Analytics.Tests.Application;

/// <summary>
/// O mapa enum → chave do jsonb é a ponte entre o contrato HTTP e o layout de <c>audio_features</c>: renomear
/// uma ponta sem a outra zeraria campos em silêncio.
/// </summary>
public sealed class AudioFeatureJsonKeyTests
{
    [Theory]
    [InlineData(AudioFeatureKind.Danceability, "Danceability")]
    [InlineData(AudioFeatureKind.Energy, "Energy")]
    [InlineData(AudioFeatureKind.Valence, "Valence")]
    [InlineData(AudioFeatureKind.Tempo, "Tempo")]
    [InlineData(AudioFeatureKind.Acousticness, "Acousticness")]
    [InlineData(AudioFeatureKind.Instrumentalness, "Instrumentalness")]
    [InlineData(AudioFeatureKind.Liveness, "Liveness")]
    [InlineData(AudioFeatureKind.Speechiness, "Speechiness")]
    [InlineData(AudioFeatureKind.Loudness, "Loudness")]
    [InlineData(AudioFeatureKind.Key, "Key")]
    [InlineData(AudioFeatureKind.Mode, "Mode")]
    [InlineData(AudioFeatureKind.TimeSignature, "TimeSignature")]
    public void Of_MapsEachFeature_ToItsPascalCaseJsonbKey(AudioFeatureKind feature, string expectedKey)
    {
        Assert.Equal(expectedKey, AudioFeatureJsonKey.Of(feature));
    }

    [Fact]
    public void Of_CoversEveryDeclaredFeature_SoANewMemberCannotShipUnmapped()
    {
        foreach (AudioFeatureKind feature in Enum.GetValues<AudioFeatureKind>())
        {
            string key = AudioFeatureJsonKey.Of(feature);
            Assert.False(string.IsNullOrWhiteSpace(key));
        }
    }

    [Fact]
    public void Of_RejectsAValueOutsideTheEnum_RatherThanMappingItToAWrongKey()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioFeatureJsonKey.Of((AudioFeatureKind)99));
    }
}
