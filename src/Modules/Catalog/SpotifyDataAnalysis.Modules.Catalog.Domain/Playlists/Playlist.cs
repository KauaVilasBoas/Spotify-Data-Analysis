using SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists.Events;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Guards;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists;

/// <summary>
/// Agregado raiz de uma <b>playlist-semente</b>: o ponto de partida da coleta. Não é um espelho fiel da
/// playlist no Spotify — é o registro de <i>o que a nossa ingestão viu</i> na última passada (quais faixas,
/// quando), que é o que dá idempotência e observabilidade ao job de coleta (E1.6).
///
/// Os ids de faixa são guardados <b>por valor</b> (sem FK/navegação para o agregado <c>Track</c>), como manda
/// a regra de referência cross-agregado.
/// </summary>
public sealed class Playlist : AggregateRoot<SpotifyPlaylistId>
{
    private readonly List<string> _trackIds;

    private Playlist(SpotifyPlaylistId id, string name, string? ownerDisplayName, List<string> trackIds)
        : base(id)
    {
        Name = name;
        OwnerDisplayName = ownerDisplayName;
        _trackIds = trackIds;
    }

    // Construtor sem parâmetros para a materialização do EF Core; a hidratação sobrescreve tudo.
    private Playlist() : base(SpotifyPlaylistId.Of("_"))
    {
        Name = string.Empty;
        _trackIds = [];
    }

    public string Name { get; private set; }

    /// <summary>Nome de exibição do dono da playlist (pode não vir na resposta da API).</summary>
    public string? OwnerDisplayName { get; private set; }

    /// <summary>Ids das faixas vistas na última coleta (por valor, deduplicados e em ordem de aparição).</summary>
    public IReadOnlyList<string> TrackIds => _trackIds.AsReadOnly();

    public int TrackCount => _trackIds.Count;

    /// <summary>Quando a última coleta desta playlist foi concluída; nulo enquanto nunca foi coletada.</summary>
    public DateTime? LastIngestedAtUtc { get; private set; }

    /// <summary>Registra a playlist como semente de coleta (ainda sem nenhuma passada de ingestão).</summary>
    public static Playlist RegisterAsSeed(SpotifyPlaylistId id, string name, string? ownerDisplayName)
    {
        Guard.AgainstNull(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));

        return new Playlist(id, name.Trim(), Normalize(ownerDisplayName), []);
    }

    /// <summary>Atualiza os metadados quando a playlist é renomeada ou muda de dono no Spotify.</summary>
    public void UpdateMetadata(string name, string? ownerDisplayName)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));

        Name = name.Trim();
        OwnerDisplayName = Normalize(ownerDisplayName);
    }

    /// <summary>
    /// Fecha um ciclo de coleta: <b>substitui</b> o conjunto de faixas conhecidas pelo que foi visto agora e
    /// carimba a data. A substituição (em vez de acumular) é o que torna a operação idempotente — rodar o
    /// job duas vezes sobre a mesma playlist leva exatamente ao mesmo estado — e faz faixas removidas da
    /// playlist saírem daqui naturalmente.
    /// </summary>
    public void RecordIngestion(IEnumerable<SpotifyTrackId> trackIds, DateTime occurredOnUtc)
    {
        Guard.AgainstNull(trackIds, nameof(trackIds));

        _trackIds.Clear();
        _trackIds.AddRange(trackIds.Select(trackId => trackId.Value).Distinct(StringComparer.Ordinal));

        LastIngestedAtUtc = occurredOnUtc;

        RaiseDomainEvent(new PlaylistIngestedDomainEvent(Id.Value, Name, _trackIds.Count, occurredOnUtc));
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
