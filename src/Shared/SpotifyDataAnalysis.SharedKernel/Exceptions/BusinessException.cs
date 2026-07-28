namespace SpotifyDataAnalysis.SharedKernel.Exceptions;

/// <summary>Domain rule violation — maps to 422 with Success=false.</summary>
public sealed class BusinessException : Exception
{
    public BusinessException(string message) : base(message) { }
}
