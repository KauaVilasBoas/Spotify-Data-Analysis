namespace SpotifyDataAnalysis.SharedKernel.Exceptions;

/// <summary>Base exception for business rule violations (domain invariants).</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }

    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}
