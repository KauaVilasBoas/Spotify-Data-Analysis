namespace SpotifyDataAnalysis.SharedKernel.Exceptions;

/// <summary>Access denied: user is authenticated but lacks permission for the operation. HTTP 403.</summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }

    public ForbiddenException()
        : base("You do not have permission to perform this operation.") { }
}
