using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SpotifyDataAnalysis.Api.Middleware;
using SpotifyDataAnalysis.SharedKernel.Exceptions;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Observability;

namespace SpotifyDataAnalysis.Api.Tests.Middleware;

/// <summary>
/// E6.9 — <see cref="ExceptionHandlingMiddleware"/> traduz <see cref="ServiceUnavailableException"/>
/// para 503 com corpo RFC 7807 e header <c>Retry-After</c> quando fornecido.
///
/// <para>Também garante que nenhum outro mapeamento existente regrediu.</para>
/// </summary>
public sealed class ExceptionHandlingMiddlewareTests
{
    // ---- stubs inline (sem mock framework) ---------------------------------------------------------------

    private sealed class FixedCorrelationId(string id) : ICorrelationIdAccessor
    {
        public string CorrelationId { get; } = id;
    }

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Test";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = "/";
    }

    // ---- helpers -----------------------------------------------------------------------------------------

    private static async Task<(int statusCode, string body, IHeaderDictionary headers)>
        InvokeAsync(Exception toThrow, bool isDevelopment = false)
    {
        RequestDelegate next = _ => throw toThrow;

        var middleware = new ExceptionHandlingMiddleware(
            next,
            NullLogger<ExceptionHandlingMiddleware>.Instance,
            new StubEnvironment(isDevelopment ? Environments.Development : Environments.Production));

        var correlationId = new FixedCorrelationId("test-trace");

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/test";

        await middleware.InvokeAsync(context, correlationId);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        return (context.Response.StatusCode, body, context.Response.Headers);
    }

    // ---- ServiceUnavailableException → 503 ---------------------------------------------------------------

    [Fact]
    public async Task ServiceUnavailableException_Returns503()
    {
        var (status, _, _) = await InvokeAsync(new ServiceUnavailableException("Index warming up."));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
    }

    [Fact]
    public async Task ServiceUnavailableException_BodyIsProblemDetails_WithCorrectType()
    {
        var (_, body, _) = await InvokeAsync(new ServiceUnavailableException("Index warming up."));

        using JsonDocument doc = JsonDocument.Parse(body);
        string? type = doc.RootElement.GetProperty("type").GetString();
        Assert.Equal(ProblemDetailsTypes.ServiceUnavailable, type);
    }

    [Fact]
    public async Task ServiceUnavailableException_BodyCarriesExceptionMessage()
    {
        const string message = "Similarity index is still being built.";
        var (_, body, _) = await InvokeAsync(new ServiceUnavailableException(message));

        using JsonDocument doc = JsonDocument.Parse(body);
        string? detail = doc.RootElement.GetProperty("detail").GetString();
        Assert.Equal(message, detail);
    }

    [Fact]
    public async Task ServiceUnavailableException_WithRetryAfterSeconds_SetsRetryAfterHeader()
    {
        var (_, _, headers) = await InvokeAsync(new ServiceUnavailableException("Warming up.", retryAfterSeconds: 30));

        Assert.Equal("30", headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task ServiceUnavailableException_WithoutRetryAfterSeconds_OmitsRetryAfterHeader()
    {
        var (_, _, headers) = await InvokeAsync(new ServiceUnavailableException("Warming up."));

        Assert.False(headers.ContainsKey("Retry-After"));
    }

    // ---- Sem regressão nos mapeamentos existentes --------------------------------------------------------

    [Fact]
    public async Task NotFoundException_StillReturns404()
    {
        var (status, _, _) = await InvokeAsync(new NotFoundException("Track not found."));

        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task BusinessException_StillReturns422()
    {
        var (status, _, _) = await InvokeAsync(new BusinessException("Domain rule violated."));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, status);
    }

    [Fact]
    public async Task UnhandledException_StillReturns500()
    {
        var (status, _, _) = await InvokeAsync(new InvalidOperationException("Boom."));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
    }
}
