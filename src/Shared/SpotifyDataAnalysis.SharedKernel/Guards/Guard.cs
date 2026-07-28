using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.SharedKernel.Guards;

/// <summary>
/// Guard clauses for precondition validation in the domain and application layers.
/// Each method throws <see cref="DomainException"/> when the condition is not met,
/// preventing invalid state from entering aggregates.
/// </summary>
public static class Guard
{
    public static T AgainstNull<T>(T? value, string parameterName)
        where T : class
    {
        if (value is null)
            throw new DomainException($"'{parameterName}' cannot be null.");

        return value;
    }

    public static string AgainstNullOrWhiteSpace(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"'{parameterName}' cannot be null or empty.");

        return value;
    }

    public static decimal AgainstNonPositive(decimal value, string parameterName)
    {
        if (value <= 0)
            throw new DomainException($"'{parameterName}' must be greater than zero. Received: {value}.");

        return value;
    }

    public static decimal AgainstNegative(decimal value, string parameterName)
    {
        if (value < 0)
            throw new DomainException($"'{parameterName}' cannot be negative. Received: {value}.");

        return value;
    }

    public static Guid AgainstEmptyGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new DomainException($"'{parameterName}' cannot be an empty Guid.");

        return value;
    }

    public static string AgainstMaxLength(string value, int maxLength, string parameterName)
    {
        if (value.Length > maxLength)
            throw new DomainException($"'{parameterName}' exceeds the maximum length of {maxLength} characters.");

        return value;
    }
}
