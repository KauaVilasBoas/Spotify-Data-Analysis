using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

/// <summary>
/// Identificador de uma faixa no Spotify (a <i>track id</i> da API) — a identidade do agregado
/// <see cref="Track"/> e a chave primária de casamento com o dataset Kaggle.
/// </summary>
public sealed class SpotifyTrackId : SpotifyResourceId
{
    private SpotifyTrackId(string value) : base(value) { }

    public static SpotifyTrackId Of(string value) => new(Normalize(value, nameof(value)));
}
