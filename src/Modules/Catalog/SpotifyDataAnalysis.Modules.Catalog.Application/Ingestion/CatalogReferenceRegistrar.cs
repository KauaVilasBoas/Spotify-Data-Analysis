using SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Albums;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Artists;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Garante que os agregados <see cref="Artist"/> e <see cref="Album"/> <b>referenciados por uma faixa</b>
/// existam no catálogo.
///
/// Existe como colaborador separado (e não dentro do handler) porque é uma responsabilidade própria e
/// reaproveitável: qualquer caminho de ingestão futuro — outra playlist, busca por artista, importação
/// incremental — precisa exatamente do mesmo "upsert por id" e não deveria reimplementá-lo.
///
/// É <b>idempotente</b> por construção: se o agregado já existe, no máximo o nome é corrigido; o perfil
/// completo (popularidade/seguidores/gêneros do artista, data de lançamento do álbum) NÃO é sobrescrito
/// aqui, porque a referência embutida na faixa não o contém e apagá-lo seria perda de informação.
///
/// O cache em memória evita reconsultar o repositório para artistas que se repetem ao longo da mesma
/// playlist (o caso comum: um artista aparece em dezenas de faixas).
/// </summary>
internal sealed class CatalogReferenceRegistrar
{
    private readonly IArtistRepository _artists;
    private readonly IAlbumRepository _albums;

    private readonly HashSet<string> _knownArtistIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _knownAlbumIds = new(StringComparer.Ordinal);

    public CatalogReferenceRegistrar(IArtistRepository artists, IAlbumRepository albums)
    {
        _artists = artists;
        _albums = albums;
    }

    /// <summary>Registra os artistas creditados numa faixa que ainda não estejam no catálogo.</summary>
    public async Task EnsureArtistsRegisteredAsync(
        IEnumerable<SpotifyArtistRef> artistRefs, CancellationToken cancellationToken = default)
    {
        foreach (SpotifyArtistRef artistRef in artistRefs)
        {
            if (string.IsNullOrWhiteSpace(artistRef.Id) || string.IsNullOrWhiteSpace(artistRef.Name))
                continue;

            if (!_knownArtistIds.Add(artistRef.Id))
                continue;

            SpotifyArtistId id = SpotifyArtistId.Of(artistRef.Id);
            Artist? existing = await _artists.GetByIdAsync(id, cancellationToken);

            if (existing is null)
                await _artists.AddAsync(Artist.RegisterFromReference(id, artistRef.Name), cancellationToken);
            else
                existing.Rename(artistRef.Name);
        }
    }

    /// <summary>Registra o álbum de uma faixa quando ele ainda não está no catálogo (ignora faixas sem álbum).</summary>
    public async Task EnsureAlbumRegisteredAsync(
        SpotifyAlbumRef? albumRef, CancellationToken cancellationToken = default)
    {
        if (albumRef is null || string.IsNullOrWhiteSpace(albumRef.Id) || string.IsNullOrWhiteSpace(albumRef.Name))
            return;

        if (!_knownAlbumIds.Add(albumRef.Id))
            return;

        SpotifyAlbumId id = SpotifyAlbumId.Of(albumRef.Id);
        Album? existing = await _albums.GetByIdAsync(id, cancellationToken);

        if (existing is null)
            await _albums.AddAsync(Album.RegisterFromReference(id, albumRef.Name), cancellationToken);
        else
            existing.Rename(albumRef.Name);
    }
}
