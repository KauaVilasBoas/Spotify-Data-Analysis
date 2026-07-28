using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

/// <summary>
/// Popularidade de uma faixa no Spotify — inteiro de <b>0 a 100</b> (o alvo do modelo de predição do E3).
/// Value object que garante a faixa válida na construção.
/// </summary>
public sealed class Popularity : ValueObject
{
    public const int Min = 0;
    public const int Max = 100;

    public int Value { get; }

    private Popularity(int value) => Value = value;

    public static Popularity Of(int value)
    {
        if (value is < Min or > Max)
            throw new DomainException($"Popularidade deve estar entre {Min} e {Max}. Recebido: {value}.");

        return new Popularity(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString();
}
