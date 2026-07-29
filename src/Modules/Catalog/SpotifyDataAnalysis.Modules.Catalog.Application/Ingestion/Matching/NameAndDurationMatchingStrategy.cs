using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Matching;

/// <summary>
/// Desambigua homônimos do mesmo artista pela <b>duração da gravação</b> (E1.9). Usa a mesma chave textual do
/// fallback (<see cref="TrackMatchKey"/>), mas, entre as faixas que colidem nessa chave, escolhe a de duração
/// mais próxima da linha do dataset — dentro de uma tolerância pequena.
///
/// Fica <b>entre</b> o casamento exato por <c>track_id</c> e o fallback textual puro (<see
/// cref="NameAndArtistMatchingStrategy"/>): quando há uma candidata na tolerância de duração, ela é uma
/// escolha mais precisa que "a primeira ocorrência" da chave; quando não há duração no CSV ou nenhuma
/// candidata bate a duração, esta estratégia <b>cede a vez</b> (devolve <see langword="null"/>) e o fallback
/// textual assume o último recurso.
///
/// A duração é uma pista, não uma identidade — por isso a tolerância: masterizações e edições da mesma
/// gravação variam alguns segundos entre fontes. Casar duas gravações genuinamente distintas exige que elas
/// tenham o mesmo artista, o mesmo título normalizado <b>e</b> durações praticamente iguais, o que é raro o
/// bastante para ser uma escolha melhor que o desempate arbitrário de hoje.
/// </summary>
internal sealed class NameAndDurationMatchingStrategy : ITrackMatchingStrategy
{
    /// <summary>
    /// Janela de duração aceita como "a mesma gravação" (±2 s). Frouxa o suficiente para absorver diferenças
    /// de masterização/arredondamento entre a API e o dataset, estreita o suficiente para separar faixas
    /// homônimas realmente distintas (que costumam diferir bem mais que isso).
    /// </summary>
    private const int DurationToleranceMs = 2_000;

    private readonly ITrackRepository _tracks;

    public NameAndDurationMatchingStrategy(ITrackRepository tracks) => _tracks = tracks;

    public TrackMatchKind Kind => TrackMatchKind.NameAndDuration;

    public async Task<Track?> MatchAsync(
        KaggleAudioFeaturesRow row, CancellationToken cancellationToken = default)
    {
        // Sem duração no dataset não há o que desambiguar: cede a vez ao fallback textual, sem inventar valor.
        if (row.DurationMs is not int durationMs)
            return null;

        TrackMatchKey matchKey = TrackMatchKey.FromArtistList(row.TrackName, row.Artists);

        if (matchKey.IsEmpty)
            return null;

        return await _tracks.FindByMatchKeyAndDurationAsync(
            matchKey, durationMs, DurationToleranceMs, cancellationToken);
    }
}
