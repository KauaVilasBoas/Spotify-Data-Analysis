using System.Globalization;
using SpotifyDataAnalysis.SharedKernel.Domain;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Albums;

/// <summary>Granularidade com que o Spotify conhece a data de lançamento de um álbum.</summary>
public enum ReleasePrecision
{
    Year = 0,
    Month = 1,
    Day = 2
}

/// <summary>
/// Data de lançamento de um álbum. A API do Spotify devolve <c>release_date</c> com <b>precisão variável</b>
/// (<c>"1975"</c>, <c>"1975-11"</c>, <c>"1975-11-21"</c>), então guardar um <c>DateOnly</c> puro inventaria
/// dia/mês que não existem no dado de origem.
///
/// Este value object preserva o texto original, a precisão e o <see cref="Year"/> — que é o recorte que a EDA
/// (E2) realmente usa (popularidade por década/ano).
/// </summary>
public sealed class ReleaseDate : ValueObject
{
    private ReleaseDate(string raw, int year, ReleasePrecision precision, DateOnly? exactDate)
    {
        Raw = raw;
        Year = year;
        Precision = precision;
        ExactDate = exactDate;
    }

    /// <summary>O texto exatamente como a API o devolveu.</summary>
    public string Raw { get; }

    /// <summary>Ano de lançamento — sempre presente (é a menor granularidade que a API garante).</summary>
    public int Year { get; }

    public ReleasePrecision Precision { get; }

    /// <summary>Data completa, apenas quando a precisão é <see cref="ReleasePrecision.Day"/>.</summary>
    public DateOnly? ExactDate { get; }

    /// <summary>
    /// Interpreta o <c>release_date</c> da API. Devolve <see langword="null"/> quando o valor está ausente ou
    /// num formato irreconhecível — data de lançamento é um dado opcional e não deve derrubar a ingestão.
    /// </summary>
    public static ReleaseDate? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string trimmed = raw.Trim();

        if (DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateOnly exact))
            return new ReleaseDate(trimmed, exact.Year, ReleasePrecision.Day, exact);

        if (DateTime.TryParseExact(trimmed, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime month))
            return new ReleaseDate(trimmed, month.Year, ReleasePrecision.Month, exactDate: null);

        if (int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int year)
            && year is >= 1000 and <= 9999)
            return new ReleaseDate(trimmed, year, ReleasePrecision.Year, exactDate: null);

        return null;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Raw;
        yield return Year;
        yield return Precision;
        yield return ExactDate;
    }

    public override string ToString() => Raw;
}
