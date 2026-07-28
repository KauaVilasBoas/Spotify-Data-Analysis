namespace SpotifyDataAnalysis.SharedKernel.Exceptions;

/// <summary>Resource not found. HTTP 404.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string resourceName, object key)
        : base($"'{resourceName}' with identifier '{key}' was not found.") { }

    public NotFoundException(string message) : base(message) { }
}
