using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// Conversores/comparadores de valor para as coleções guardadas <b>por valor</b> nos agregados (ids de
/// artista numa playlist, gêneros de um artista, créditos de artista numa faixa). Todas viram uma única
/// coluna <c>jsonb</c> — não são entidades, não têm identidade própria e nunca são consultadas isoladamente,
/// então uma tabela filha só traria join sem benefício.
///
/// Centralizado aqui porque o par converter+comparer é fácil de errar: <b>sem o
/// <see cref="ValueComparer{T}"/> o EF compara as listas por referência</b> e nunca detecta mutação, e o
/// snapshot precisa ser uma cópia (senão o "antes" e o "depois" são o mesmo objeto).
///
/// A serialização dos value objects passa por DTOs privados desta camada: o Domain permanece livre de
/// atributos/convenções de serialização.
/// </summary>
internal static class JsonListConverters
{
    private static readonly JsonSerializerOptions Options = new();

    /// <summary>Lista de strings (ex.: ids de faixa numa playlist, gêneros de um artista) → <c>jsonb</c>.</summary>
    public static ValueConverter<List<string>, string> Strings { get; } =
        new(
            list => JsonSerializer.Serialize(list, Options),
            json => JsonSerializer.Deserialize<List<string>>(json, Options) ?? new List<string>());

    public static ValueComparer<List<string>> StringsComparer { get; } =
        new(
            (left, right) => (left ?? new List<string>()).SequenceEqual(right ?? new List<string>()),
            list => list.Aggregate(0, (hash, value) => HashCode.Combine(hash, value.GetHashCode())),
            list => list.ToList());

    /// <summary>Créditos de artista de uma faixa (id + nome) → <c>jsonb</c>.</summary>
    public static ValueConverter<List<TrackArtist>, string> TrackArtists { get; } =
        new(
            list => JsonSerializer.Serialize(
                list.Select(artist => new TrackArtistJson(artist.Id, artist.Name)).ToList(), Options),
            json => (JsonSerializer.Deserialize<List<TrackArtistJson>>(json, Options) ?? new List<TrackArtistJson>())
                .Select(dto => TrackArtist.Of(dto.Id, dto.Name))
                .ToList());

    public static ValueComparer<List<TrackArtist>> TrackArtistsComparer { get; } =
        new(
            (left, right) => (left ?? new List<TrackArtist>()).SequenceEqual(right ?? new List<TrackArtist>()),
            list => list.Aggregate(0, (hash, artist) => HashCode.Combine(hash, artist.GetHashCode())),
            list => list.ToList());

    /// <summary>Forma serializada de <see cref="TrackArtist"/> — detalhe de persistência, não do domínio.</summary>
    private sealed record TrackArtistJson(string Id, string Name);
}
