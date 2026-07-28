using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.SharedKernel.Exceptions;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Observability;

namespace SpotifyDataAnalysis.Api.Middleware;

/// <summary>
/// Single, uniform error boundary: translates unhandled exceptions into RFC 7807
/// <see cref="ProblemDetails"/> responses.
///
/// <para>Mapping:</para>
/// <list type="bullet">
///   <item><see cref="BusinessException"/> → 422, type <c>business-rule-violation</c></item>
///   <item><see cref="NotFoundException"/> → 404, type <c>not-found</c></item>
///   <item><see cref="ConflictException"/> → 409, type <c>conflict</c></item>
///   <item><see cref="ForbiddenException"/> → 403, type <c>forbidden</c></item>
///   <item><see cref="ValidationException"/> (FluentValidation) → 400, type <c>validation-error</c>,
///     with an <c>errors</c> map of field → messages</item>
///   <item>any other exception → 500, type <c>internal-error</c> (detail hidden outside development)</item>
/// </list>
///
/// Every problem carries a <c>traceId</c> from <see cref="ICorrelationIdAccessor"/> and the request
/// <c>instance</c> path, so a client-reported error can be found in the logs by correlation id. Controllers
/// therefore never map errors to HTTP themselves — they map only the happy path (still via
/// <c>ApiResult&lt;T&gt;</c>) and let domain/application exceptions bubble up here.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationIdAccessor correlationIdAccessor)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            ProblemDetails problem = BuildValidationProblem(ex);
            await WriteAsync(context, problem, correlationIdAccessor);
        }
        catch (BusinessException ex)
        {
            ProblemDetails problem = Build(
                StatusCodes.Status422UnprocessableEntity,
                ProblemDetailsTypes.BusinessRuleViolation,
                "Business Rule Violation",
                ex.Message);
            await WriteAsync(context, problem, correlationIdAccessor);
        }
        catch (NotFoundException ex)
        {
            ProblemDetails problem = Build(
                StatusCodes.Status404NotFound,
                ProblemDetailsTypes.NotFound,
                "Not Found",
                ex.Message);
            await WriteAsync(context, problem, correlationIdAccessor);
        }
        catch (ConflictException ex)
        {
            ProblemDetails problem = Build(
                StatusCodes.Status409Conflict,
                ProblemDetailsTypes.Conflict,
                "Conflict",
                ex.Message);
            await WriteAsync(context, problem, correlationIdAccessor);
        }
        catch (ForbiddenException ex)
        {
            ProblemDetails problem = Build(
                StatusCodes.Status403Forbidden,
                ProblemDetailsTypes.Forbidden,
                "Forbidden",
                ex.Message);
            await WriteAsync(context, problem, correlationIdAccessor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing {Method} {Path}.",
                context.Request.Method, context.Request.Path);

            // Never leak internal details in production — only surface the message in development.
            string detail = _environment.IsDevelopment()
                ? ex.Message
                : "An unexpected error occurred.";

            ProblemDetails problem = Build(
                StatusCodes.Status500InternalServerError,
                ProblemDetailsTypes.InternalError,
                "Internal Server Error",
                detail);
            await WriteAsync(context, problem, correlationIdAccessor);
        }
    }

    private static ProblemDetails Build(int status, string type, string title, string detail) => new()
    {
        Type = type,
        Title = title,
        Status = status,
        Detail = detail
    };

    private static ValidationProblemDetails BuildValidationProblem(ValidationException exception)
    {
        Dictionary<string, string[]> errors = exception.Errors
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray());

        return new ValidationProblemDetails(errors)
        {
            Type = ProblemDetailsTypes.ValidationError,
            Title = "Validation Error",
            Status = StatusCodes.Status400BadRequest,
            Detail = "One or more validation errors occurred."
        };
    }

    private static Task WriteAsync(
        HttpContext context,
        ProblemDetails problem,
        ICorrelationIdAccessor correlationIdAccessor)
    {
        // traceId ties the error back to every log line of this request (correlation id); instance is the
        // request path the problem occurred on (RFC 7807).
        problem.Extensions["traceId"] = correlationIdAccessor.CorrelationId;
        problem.Instance = context.Request.Path;

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = HttpConstants.ContentTypes.ProblemJson;

        return context.Response.WriteAsJsonAsync(
            problem, problem.GetType(), options: null, contentType: HttpConstants.ContentTypes.ProblemJson);
    }
}
