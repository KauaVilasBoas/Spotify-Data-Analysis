using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeamento EF do agregado <see cref="Track"/>.
///
/// Decisões:
/// <list type="bullet">
///   <item><see cref="SpotifyTrackId"/> e <see cref="Popularity"/> → colunas simples via value converter
///   (round-trip pela factory validada; um valor persistido inválido falha rápido).</item>
///   <item><see cref="Track.ArtistIds"/> (ids por valor) → uma coluna <c>jsonb</c> <c>artist_ids</c>,
///   mapeada pelo campo de apoio; a propriedade read-only é ignorada.</item>
///   <item><see cref="Track.AudioFeatures"/> → owned type serializado como JSON numa única coluna
///   <c>audio_features</c> (opcional; nulo até ser casado com o dataset externo).</item>
/// </list>
/// </summary>
internal sealed class TrackConfiguration : IEntityTypeConfiguration<Track>
{
    private static readonly ValueConverter<SpotifyTrackId, string> TrackIdConverter =
        new(id => id.Value, value => SpotifyTrackId.Of(value));

    private static readonly ValueConverter<Popularity, int> PopularityConverter =
        new(popularity => popularity.Value, value => Popularity.Of(value));

    private static readonly ValueConverter<List<string>, string> ArtistIdsConverter =
        new(
            list => JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
            json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>());

    private static readonly ValueComparer<List<string>> ArtistIdsComparer =
        new(
            (left, right) => (left ?? new List<string>()).SequenceEqual(right ?? new List<string>()),
            list => list.Aggregate(0, (hash, value) => HashCode.Combine(hash, value.GetHashCode())),
            list => list.ToList());

    public void Configure(EntityTypeBuilder<Track> builder)
    {
        builder.ToTable("tracks");

        builder.HasKey(track => track.Id);
        builder.Property(track => track.Id)
            .HasColumnName("id")
            .HasConversion(TrackIdConverter)
            .HasMaxLength(64)
            .ValueGeneratedNever();

        builder.Property(track => track.Name)
            .IsRequired()
            .HasMaxLength(400);

        builder.Property(track => track.Popularity)
            .HasColumnName("popularity")
            .HasConversion(PopularityConverter)
            .IsRequired();

        builder.Property(track => track.DurationMs).IsRequired();
        builder.Property(track => track.Explicit).IsRequired();
        builder.Property(track => track.AlbumId).HasMaxLength(64);

        // ArtistIds: mapeado pelo campo de apoio como jsonb; a propriedade read-only é ignorada.
        builder.Ignore(track => track.ArtistIds);
        builder.Property<List<string>>("_artistIds")
            .HasColumnName("artist_ids")
            .HasColumnType("jsonb")
            .HasConversion(ArtistIdsConverter, ArtistIdsComparer)
            .IsRequired();

        // AudioFeatures (E1.4): owned type opcional serializado como JSON numa única coluna "audio_features".
        builder.OwnsOne(track => track.AudioFeatures, owned => owned.ToJson("audio_features"));
    }
}
