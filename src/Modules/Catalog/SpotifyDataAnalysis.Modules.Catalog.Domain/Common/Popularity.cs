using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Common;

/// <summary>
/// Popularidade no Spotify — inteiro de <b>0 a 100</b>. Aplica-se tanto a uma faixa (o alvo do modelo de
/// predição do E3) quanto a um artista (uma das features mais explicativas do mesmo modelo), por isso vive
/// no núcleo comum do módulo e não dentro de um agregado específico.
/// </summary>
public sealed class Popularity : ValueObject
{
    public const int Min = 0;
    public const int Max = 100;

    /// <summary>Popularidade desconhecida — o valor neutro de um recurso ainda não enriquecido pela API.</summary>
    public static Popularity Unknown { get; } = new(Min);

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
