namespace SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;

/// <summary>
/// Porta que fornece um <b>access token</b> válido da Spotify Web API para autenticar as chamadas do
/// <see cref="ISpotifyClient"/>. A implementação real (fluxo <i>Client Credentials</i> com cache/refresh)
/// é entregue no card <b>E0.3</b>; até lá, um stub na Infrastructure sinaliza a ausência de configuração.
/// </summary>
public interface ISpotifyTokenProvider
{
    /// <summary>Retorna um access token válido (renovando-o quando necessário).</summary>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
