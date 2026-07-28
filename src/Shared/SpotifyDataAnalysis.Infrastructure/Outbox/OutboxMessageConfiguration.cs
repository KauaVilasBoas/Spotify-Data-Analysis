using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SpotifyDataAnalysis.Infrastructure.Outbox;

/// <summary>
/// EF Core configuration for <see cref="OutboxMessage"/>.
/// A single partial index (ix_outbox_messages_pending) covers the dispatcher's exact predicate
/// (processed_at_utc IS NULL AND dead_lettered_at_utc IS NULL) ordered by occurred_on_utc, so both the
/// pending scan and the FIFO Take() are served by one lean index that automatically excludes processed
/// and dead-lettered rows (a Postgres partial index).
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.EventType)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(m => m.Payload)
            .IsRequired();

        builder.Property(m => m.OccurredOnUtc)
            .IsRequired();

        builder.Property(m => m.CreatedAtUtc)
            .IsRequired();

        builder.Property(m => m.ProcessedAtUtc)
            .IsRequired(false);

        builder.Property(m => m.AttemptCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(m => m.DeadLetteredAtUtc)
            .IsRequired(false);

        builder.Property(m => m.Error)
            .HasMaxLength(2048)
            .IsRequired(false);

        // Partial index matching the dispatcher's pending predicate: "pending" means BOTH not-processed AND
        // not-dead-lettered, so one filtered index ordered by occurred_on_utc serves the WHERE clause and
        // the FIFO Take() together, and shrinks as messages leave the pending set.
        builder.HasIndex(m => m.OccurredOnUtc)
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter("processed_at_utc IS NULL AND dead_lettered_at_utc IS NULL");
    }
}
