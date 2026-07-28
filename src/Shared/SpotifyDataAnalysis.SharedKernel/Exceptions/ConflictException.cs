namespace SpotifyDataAnalysis.SharedKernel.Exceptions;

/// <summary>State conflict: the operation cannot be completed due to inconsistency or duplication. HTTP 409.</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}
