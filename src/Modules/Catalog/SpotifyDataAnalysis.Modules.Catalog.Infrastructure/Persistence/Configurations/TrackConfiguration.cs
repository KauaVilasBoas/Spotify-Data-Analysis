using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeamento EF do agregado <see cref="Track"/>.
///
/// Decisões:
/// <list type="bullet">
///   <item><see cref="SpotifyTrackId"/> e <see cref="Popularity"/> → colunas simples via value converter
///   (round-trip pela factory validada; um valor persistido inválido falha rápido).</item>
///   <item><see cref="Track.Artists"/> (créditos por valor) → uma coluna <c>jsonb</c> <c>artists</c>,
///   mapeada pelo campo de apoio; as propriedades derivadas read-only são ignoradas.</item>
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

    private static readonly ValueConverter<TrackMatchKey, string> MatchKeyConverter =
        new(key => key.Value, value => TrackMatchKey.FromNormalized(value));

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

        // MatchKey: coluna indexada (não-única — homônimos do mesmo artista colidem por natureza) que
        // sustenta o fallback de casamento com o dataset externo sem varredura de tabela.
        builder.Property(track => track.MatchKey)
            .HasColumnName("match_key")
            .HasConversion(MatchKeyConverter)
            .HasMaxLength(800)
            .IsRequired();

        builder.HasIndex(track => track.MatchKey).HasDatabaseName("ix_tracks_match_key");

        builder.Property(track => track.DurationMs).IsRequired();
        builder.Property(track => track.Explicit).IsRequired();
        builder.Property(track => track.AlbumId).HasMaxLength(64);

        // Artists: mapeado pelo campo de apoio como jsonb; as projeções read-only não viram coluna.
        builder.Ignore(track => track.Artists);
        builder.Ignore(track => track.ArtistIds);
        builder.Ignore(track => track.PrimaryArtist);
        builder.Property<List<TrackArtist>>("_artists")
            .HasColumnName("artists")
            .HasColumnType("jsonb")
            .HasConversion(JsonListConverters.TrackArtists, JsonListConverters.TrackArtistsComparer)
            .IsRequired();

        // AudioFeatures (E1.4): owned type opcional serializado como JSON numa única coluna "audio_features".
        builder.OwnsOne(track => track.AudioFeatures, owned => owned.ToJson("audio_features"));
    }
}
