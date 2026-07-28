using Microsoft.EntityFrameworkCore;
using SpotifyDataAnalysis.Infrastructure.Outbox;
using SpotifyDataAnalysis.Infrastructure.Persistence;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Albums;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Artists;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence.Configurations;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// DbContext do módulo Catalog (write-side). Gerencia o agregado <see cref="Track"/> no schema
/// <c>catalog</c> e participa do Transactional Outbox (<see cref="IOutboxDbContext"/>): os domain events
/// (ex.: TrackRegistered) são traduzidos e gravados em <c>catalog.outbox_messages</c> na MESMA transação
/// da mudança do agregado, via <see cref="EfUnitOfWork{TContext}"/>.
/// </summary>
public sealed class CatalogDbContext : SpotifyDbContextBase, IOutboxDbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options) { }

    public DbSet<Track> Tracks => Set<Track>();

    public DbSet<Artist> Artists => Set<Artist>();

    public DbSet<Album> Albums => Set<Album>();

    public DbSet<Playlist> Playlists => Set<Playlist>();

    /// <inheritdoc />
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Todas as tabelas do módulo (agregados + outbox) vivem no schema "catalog".
        modelBuilder.HasDefaultSchema("catalog");

        modelBuilder.ApplyConfiguration(new TrackConfiguration());
        modelBuilder.ApplyConfiguration(new ArtistConfiguration());
        modelBuilder.ApplyConfiguration(new AlbumConfiguration());
        modelBuilder.ApplyConfiguration(new PlaylistConfiguration());

        // Outbox no schema do módulo → o write do agregado e o do outbox compartilham uma transação.
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());

        // Convenção snake_case aplicada pela base DEPOIS das configurations (vê todas as entidades mapeadas).
        base.OnModelCreating(modelBuilder);
    }
}
