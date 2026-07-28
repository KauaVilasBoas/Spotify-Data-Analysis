namespace SpotifyDataAnalysis.SharedKernel.Http;

/// <summary>
/// Uniform response envelope for controller actions that return a payload.
/// A negative outcome is never produced by the controller: it flows from an exception
/// captured by the API's exception-handling middleware.
/// </summary>
/// <param name="Success">True when the operation completed successfully.</param>
/// <param name="Message">Human-readable outcome message.</param>
/// <param name="Data">Payload returned to the caller.</param>
public sealed record ApiResult<T>(bool Success, string Message, T? Data = default);

/// <summary>
/// Uniform response envelope for controller actions that return no payload.
/// </summary>
/// <param name="Success">True when the operation completed successfully.</param>
/// <param name="Message">Human-readable outcome message.</param>
public sealed record ApiResult(bool Success, string Message);
