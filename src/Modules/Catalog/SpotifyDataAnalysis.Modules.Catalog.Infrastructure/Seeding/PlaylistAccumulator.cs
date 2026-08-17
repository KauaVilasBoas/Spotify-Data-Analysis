namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Seeding;

/// <summary>Uma playlist já acumulada, pronta para virar agregado: id derivado, nome e as faixas do catálogo que casaram.</summary>
/// <param name="PlaylistId">Id derivado (determinístico) de <c>user_id</c>+<c>playlistname</c>.</param>
/// <param name="Name">Nome da playlist, como o Pichl o traz.</param>
/// <param name="TrackIds">Ids de faixa do catálogo, na ordem de aparição e sem repetição.</param>
internal sealed record AccumulatedPlaylist(string PlaylistId, string Name, IReadOnlyList<string> TrackIds);

/// <summary>
/// Acumula as linhas do Pichl em playlists antes de persistir. Guarda, por playlist, os <c>track_id</c>s do
/// catálogo que casaram — deduplicados e em ordem de aparição, a mesma disciplina do
/// <see cref="Domain.Playlists.Playlist.RecordIngestion"/> (que também deduplica; aqui a dedup é adiantada para
/// não segurar ids repetidos na memória durante a varredura).
///
/// <para><b>Teto de playlists (DP-3):</b> ao atingir o limite de playlists DISTINTAS abertas, o acumulador para
/// de admitir playlists novas, mas continua aceitando faixas para as que já abriu — evita gravar uma playlist
/// truncada só porque o resto das suas faixas veio depois do limite. Teto não-positivo desliga o corte.</para>
///
/// <para><b>Custo de memória:</b> O(faixas casadas das playlists abertas). É o teto, e não o tamanho do arquivo,
/// que dimensiona a memória — o insight que torna viável varrer 12,9M linhas com um recorte controlado.</para>
/// </summary>
internal sealed class PlaylistAccumulator
{
    private readonly Dictionary<string, Entry> _playlists = new(StringComparer.Ordinal);
    private readonly int _maxPlaylists;

    public PlaylistAccumulator(int maxPlaylists) => _maxPlaylists = maxPlaylists;

    /// <summary>Quantas playlists distintas foram abertas (com ou sem faixas casadas).</summary>
    public long PlaylistsSeen => _playlists.Count;

    /// <summary>
    /// Registra a existência de uma playlist sem lhe adicionar faixa (a linha não casou). Respeita o teto: se ele
    /// já foi atingido e a playlist é nova, a chamada é ignorada.
    /// </summary>
    public void TouchPlaylist(string playlistId, string name) => GetOrOpen(playlistId, name);

    /// <summary>
    /// Adiciona um <c>track_id</c> do catálogo à playlist, abrindo-a se necessário (respeitando o teto). Ids
    /// repetidos na mesma playlist são ignorados — a ordem de primeira aparição é preservada.
    /// </summary>
    public void AddTrack(string playlistId, string name, string trackId)
    {
        Entry? entry = GetOrOpen(playlistId, name);
        if (entry is null)
            return;

        if (entry.SeenTrackIds.Add(trackId))
            entry.TrackIds.Add(trackId);
    }

    /// <summary>Entrega as playlists acumuladas e ESVAZIA o acumulador — a memória é liberada à medida que se persiste.</summary>
    public IEnumerable<AccumulatedPlaylist> Drain()
    {
        foreach ((string id, Entry entry) in _playlists)
            yield return new AccumulatedPlaylist(id, entry.Name, entry.TrackIds);

        _playlists.Clear();
    }

    private Entry? GetOrOpen(string playlistId, string name)
    {
        if (_playlists.TryGetValue(playlistId, out Entry? existing))
            return existing;

        if (_maxPlaylists > 0 && _playlists.Count >= _maxPlaylists)
            return null;

        var entry = new Entry(name);
        _playlists[playlistId] = entry;
        return entry;
    }

    private sealed class Entry
    {
        public Entry(string name) => Name = name;

        public string Name { get; }

        public List<string> TrackIds { get; } = [];

        public HashSet<string> SeenTrackIds { get; } = new(StringComparer.Ordinal);
    }
}
