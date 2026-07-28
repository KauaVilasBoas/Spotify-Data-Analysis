using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Guards;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

/// <summary>
/// Identificador de uma faixa no Spotify (a <i>track id</i> da API). Value object com igualdade estrutural
/// e validação de não-vazio — é a identidade do agregado <see cref="Track"/>.
/// </summary>
public sealed class SpotifyTrackId : ValueObject
{
    public string Value { get; }

    private SpotifyTrackId(string value) => Value = value;

    public static SpotifyTrackId Of(string value)
    {
        Guard.AgainstNullOrWhiteSpace(value, nameof(value));
        return new SpotifyTrackId(value.Trim());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
