using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeamento EF do agregado <see cref="Playlist"/> (semente de coleta). Os ids de faixa vão para uma coluna
/// <c>jsonb</c> <c>track_ids</c> — referência por valor, sem FK para <c>tracks</c>, como manda a regra de
/// referência cross-agregado.
/// </summary>
internal sealed class PlaylistConfiguration : IEntityTypeConfiguration<Playlist>
{
    private static readonly ValueConverter<SpotifyPlaylistId, string> PlaylistIdConverter =
        new(id => id.Value, value => SpotifyPlaylistId.Of(value));

    public void Configure(EntityTypeBuilder<Playlist> builder)
    {
        builder.ToTable("playlists");

        builder.HasKey(playlist => playlist.Id);
        builder.Property(playlist => playlist.Id)
            .HasColumnName("id")
            .HasConversion(PlaylistIdConverter)
            .HasMaxLength(64)
            .ValueGeneratedNever();

        builder.Property(playlist => playlist.Name)
            .IsRequired()
            .HasMaxLength(400);

        builder.Property(playlist => playlist.OwnerDisplayName).HasMaxLength(400);
        builder.Property(playlist => playlist.LastIngestedAtUtc);

        // Derivada do campo de apoio — não é coluna.
        builder.Ignore(playlist => playlist.TrackCount);

        builder.Ignore(playlist => playlist.TrackIds);
        builder.Property<List<string>>("_trackIds")
            .HasColumnName("track_ids")
            .HasColumnType("jsonb")
            .HasConversion(JsonListConverters.Strings, JsonListConverters.StringsComparer)
            .IsRequired();
    }
}
