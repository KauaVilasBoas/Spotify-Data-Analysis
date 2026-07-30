using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Artists;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeamento EF do agregado <see cref="Artist"/>: id e popularidade como colunas simples via value
/// converter (round-trip pelas factories validadas) e os gêneros numa coluna <c>jsonb</c>.
/// </summary>
internal sealed class ArtistConfiguration : IEntityTypeConfiguration<Artist>
{
    private static readonly ValueConverter<SpotifyArtistId, string> ArtistIdConverter =
        new(id => id.Value, value => SpotifyArtistId.Of(value));

    private static readonly ValueConverter<Popularity, int> PopularityConverter =
        new(popularity => popularity.Value, value => Popularity.Of(value));

    public void Configure(EntityTypeBuilder<Artist> builder)
    {
        builder.ToTable("artists");

        builder.HasKey(artist => artist.Id);
        builder.Property(artist => artist.Id)
            .HasColumnName("id")
            .HasConversion(ArtistIdConverter)
            .HasMaxLength(64)
            .ValueGeneratedNever();

        builder.Property(artist => artist.Name)
            .IsRequired()
            .HasMaxLength(400);

        builder.Property(artist => artist.Popularity)
            .HasColumnName("popularity")
            .HasConversion(PopularityConverter)
            .IsRequired();

        builder.Property(artist => artist.Followers).IsRequired();
        builder.Property(artist => artist.IsEnriched).IsRequired();

        builder.Property(artist => artist.EnrichmentAttempts)
            .HasColumnName("enrichment_attempts")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(artist => artist.LastEnrichmentAttemptUtc)
            .HasColumnName("last_enrichment_attempt_utc");

        // Fila de pendentes do enriquecimento (E1.8): !is_enriched + tentativas abaixo do teto, ordenada por
        // tentativas e id. O índice cobre exatamente esse acesso e mantém a varredura barata conforme o
        // catálogo cresce.
        builder.HasIndex(artist => new { artist.IsEnriched, artist.EnrichmentAttempts })
            .HasDatabaseName("ix_artists_enrichment_pending");

        // Genres: mapeado pelo campo de apoio como jsonb; a propriedade read-only é ignorada.
        builder.Ignore(artist => artist.Genres);
        builder.Property<List<string>>("_genres")
            .HasColumnName("genres")
            .HasColumnType("jsonb")
            .HasConversion(JsonListConverters.Strings, JsonListConverters.StringsComparer)
            .IsRequired();
    }
}
