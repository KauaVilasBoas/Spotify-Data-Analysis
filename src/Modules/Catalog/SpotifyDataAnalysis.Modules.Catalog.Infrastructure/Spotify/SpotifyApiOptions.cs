namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Spotify;

/// <summary>
/// Configuração do cliente da Spotify Web API (seção <c>Spotify</c>). As URLs têm defaults públicos;
/// <see cref="ClientId"/>/<see cref="ClientSecret"/> vêm de <b>User Secrets/ambiente</b> — nunca do repo —
/// e só são usados pelo provider de token real (card E0.3).
/// </summary>
public sealed class SpotifyApiOptions
{
    public const string SectionName = "Spotify";

    /// <summary>Base da Web API (com barra final; usada como <c>HttpClient.BaseAddress</c>).</summary>
    public string BaseUrl { get; init; } = "https://api.spotify.com/v1/";

    /// <summary>Endpoint do token (fluxo Client Credentials — consumido no E0.3).</summary>
    public string AuthUrl { get; init; } = "https://accounts.spotify.com/api/token";

    /// <summary>Client id do app Spotify (User Secrets/env).</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Client secret do app Spotify (User Secrets/env).</summary>
    public string ClientSecret { get; init; } = string.Empty;
}
