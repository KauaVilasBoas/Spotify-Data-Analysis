using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Spotify;

/// <summary>
/// <see cref="DelegatingHandler"/> de resiliência para as chamadas à Spotify Web API (E0.4): re-tenta
/// falhas <b>transitórias</b> — HTTP <c>429</c> e <c>5xx</c> (500/502/503/504) e exceções de rede
/// (<see cref="HttpRequestException"/>) — com <b>backoff exponencial</b>, e respeita o cabeçalho
/// <c>Retry-After</c> no 429 (espera o tempo indicado em vez do backoff). Respostas não-transitórias
/// (2xx e 4xx como 400/401/403/404) passam direto, sem retry. O atraso é injetável para testes.
/// </summary>
public sealed class SpotifyResilienceHandler : DelegatingHandler
{
    private static readonly HashSet<HttpStatusCode> TransientStatuses = new()
    {
        HttpStatusCode.TooManyRequests,     // 429
        HttpStatusCode.InternalServerError, // 500
        HttpStatusCode.BadGateway,          // 502
        HttpStatusCode.ServiceUnavailable,  // 503
        HttpStatusCode.GatewayTimeout       // 504
    };

    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public SpotifyResilienceHandler(IOptions<SpotifyApiOptions> options)
        : this(
            options.Value.MaxRetries,
            TimeSpan.FromMilliseconds(options.Value.RetryBaseDelayMilliseconds),
            Task.Delay)
    {
    }

    /// <summary>Construtor testável: injeta um atraso no-op e controla os parâmetros (sem espera real).</summary>
    internal SpotifyResilienceHandler(
        int maxRetries, TimeSpan baseDelay, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _maxRetries = maxRetries < 0 ? 0 : maxRetries;
        _baseDelay = baseDelay;
        _delay = delay;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer do corpo (se houver) para reenviar em cada tentativa — um HttpRequestMessage só pode ser
        // enviado uma vez. As chamadas da Web API são GET (corpo nulo), mas isto mantém o handler genérico.
        byte[]? body = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        for (int attempt = 0; ; attempt++)
        {
            using HttpRequestMessage attemptRequest = Clone(request, body);

            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(attemptRequest, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < _maxRetries)
            {
                await _delay(Backoff(attempt), cancellationToken);
                continue;
            }

            if (!TransientStatuses.Contains(response.StatusCode) || attempt >= _maxRetries)
                return response;

            TimeSpan wait = response.StatusCode == HttpStatusCode.TooManyRequests
                ? RetryAfter(response) ?? Backoff(attempt)
                : Backoff(attempt);

            response.Dispose();
            await _delay(wait, cancellationToken);
        }
    }

    private TimeSpan Backoff(int attempt)
        => TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt));

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
            return null;

        if (retryAfter.Delta is { } delta)
            return delta;

        if (retryAfter.Date is { } date)
        {
            TimeSpan until = date - DateTimeOffset.UtcNow;
            return until > TimeSpan.Zero ? until : TimeSpan.Zero;
        }

        return null;
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request, byte[]? body)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };

        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (body is not null)
        {
            var content = new ByteArrayContent(body);
            if (request.Content is not null)
                foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            clone.Content = content;
        }

        return clone;
    }
}
