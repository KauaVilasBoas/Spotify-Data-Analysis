using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;
using SpotifyDataAnalysis.SharedKernel.Time;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Spotify;

/// <summary>
/// Provider de token da Spotify via fluxo <b>Client Credentials</b> (server-to-server): autentica por
/// <c>Basic</c> (client_id:client_secret em Base64) no endpoint <see cref="SpotifyApiOptions.AuthUrl"/>,
/// obtém um <c>access_token</c> + <c>expires_in</c>, e o mantém em cache — reusando enquanto válido e
/// renovando pouco antes de expirar (margem de segurança). Registrado como <b>singleton</b> para que o
/// cache valha no processo inteiro, e serializa refreshes concorrentes com um <see cref="SemaphoreSlim"/>
/// (double-check). O tempo vem do <see cref="IClock"/> (testável).
/// </summary>
public sealed class SpotifyClientCredentialsTokenProvider : ISpotifyTokenProvider
{
    /// <summary>Renova o token esta folga antes do vencimento real, evitando usar um token quase expirado.</summary>
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SpotifyApiOptions _options;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _accessToken;
    private DateTime _expiresAtUtc;

    public SpotifyClientCredentialsTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<SpotifyApiOptions> options,
        IClock clock)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (IsCurrentTokenValid())
            return _accessToken!;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Double-check: outra thread pode ter renovado enquanto esperávamos o semáforo.
            if (IsCurrentTokenValid())
                return _accessToken!;

            await RefreshAsync(cancellationToken);
            return _accessToken!;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsCurrentTokenValid()
        => _accessToken is not null && _clock.UtcNow < _expiresAtUtc;

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new InvalidOperationException(
                "Credenciais do Spotify ausentes. Configure 'Spotify:ClientId' e 'Spotify:ClientSecret' " +
                "em User Secrets ou variáveis de ambiente (nunca no repositório).");

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.AuthUrl);

        string basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        HttpClient http = _httpClientFactory.CreateClient();
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        TokenResponse? token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
            throw new InvalidOperationException("Resposta de token do Spotify inválida (sem access_token).");

        _accessToken = token.AccessToken;
        _expiresAtUtc = _clock.UtcNow.AddSeconds(token.ExpiresInSeconds) - ExpiryMargin;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresInSeconds);
}
