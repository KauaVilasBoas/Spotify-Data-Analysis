using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Albums;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeamento EF do agregado <see cref="Album"/>.
///
/// A <see cref="ReleaseDate"/> vira um owned type com colunas próprias (texto cru, ano e precisão) em vez de
/// JSON: o ano é o recorte que a EDA (E2) filtra e agrupa, e como coluna ele é indexável.
/// </summary>
internal sealed class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    private static readonly ValueConverter<SpotifyAlbumId, string> AlbumIdConverter =
        new(id => id.Value, value => SpotifyAlbumId.Of(value));

    public void Configure(EntityTypeBuilder<Album> builder)
    {
        builder.ToTable("albums");

        builder.HasKey(album => album.Id);
        builder.Property(album => album.Id)
            .HasColumnName("id")
            .HasConversion(AlbumIdConverter)
            .HasMaxLength(64)
            .ValueGeneratedNever();

        builder.Property(album => album.Name)
            .IsRequired()
            .HasMaxLength(400);

        builder.Property(album => album.TotalTracks).IsRequired();
        builder.Property(album => album.IsEnriched).IsRequired();

        builder.OwnsOne(album => album.ReleaseDate, releaseDate =>
        {
            releaseDate.Property(date => date.Raw)
                .HasColumnName("release_date_raw")
                .HasMaxLength(10);

            releaseDate.Property(date => date.Year).HasColumnName("release_year");

            releaseDate.Property(date => date.Precision)
                .HasColumnName("release_precision")
                .HasConversion<string>()
                .HasMaxLength(10);

            releaseDate.Property(date => date.ExactDate).HasColumnName("release_date");

            releaseDate.HasIndex(date => date.Year).HasDatabaseName("ix_albums_release_year");
        });
    }
}
