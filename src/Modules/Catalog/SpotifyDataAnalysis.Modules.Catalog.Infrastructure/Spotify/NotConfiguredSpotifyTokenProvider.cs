using SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Spotify;

/// <summary>
/// Provider de token <b>placeholder</b> para o E0.2: mantém o seam de autenticação injetável sem ainda
/// implementar o fluxo Client Credentials (que é o card <b>E0.3</b>). Lança uma exceção clara se alguém
/// tentar autenticar de fato — o registro é substituído pelo provider real no E0.3.
/// </summary>
public sealed class NotConfiguredSpotifyTokenProvider : ISpotifyTokenProvider
{
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(
            "Autenticação com o Spotify ainda não configurada. O fluxo Client Credentials (obtenção/cache/" +
            "refresh do token) é implementado no card E0.3. Configure 'Spotify:ClientId' e " +
            "'Spotify:ClientSecret' em User Secrets e registre o provider real no lugar deste.");
}
