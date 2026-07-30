using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Guards;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

/// <summary>
/// Crédito de artista numa faixa: o <b>id</b> (referência por valor ao agregado <c>Artist</c>) somado ao
/// <b>nome</b> tal como a API o devolveu no momento da ingestão.
///
/// Guardar o nome junto do id é deliberado e não é redundância: o casamento com o dataset Kaggle (E1.4)
/// depende de "nome da faixa + nome do artista" quando o <c>track_id</c> não bate, e resolvê-lo por join no
/// agregado <c>Artist</c> a cada linha do CSV (~114k) seria inviável. A ordem da lista preserva a ordem da
/// API — o primeiro é o artista principal.
/// </summary>
public sealed class TrackArtist : ValueObject
{
    private TrackArtist(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }

    public static TrackArtist Of(string id, string name)
    {
        Guard.AgainstNullOrWhiteSpace(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));

        return new TrackArtist(id.Trim(), name.Trim());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Id;
        yield return Name;
    }

    public override string ToString() => Name;
}
