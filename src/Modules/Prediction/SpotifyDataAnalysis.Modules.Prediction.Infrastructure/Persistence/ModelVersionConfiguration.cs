using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Models;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Persistence;

/// <summary>
/// Mapeamento EF do agregado <see cref="ModelVersion"/>.
///
/// <para>O <c>Id</c> é identity: a numeração sequencial das versões é responsabilidade do banco, e não de uma
/// contagem em memória que corre risco de colidir sob concorrência.</para>
/// </summary>
internal sealed class ModelVersionConfiguration : IEntityTypeConfiguration<ModelVersion>
{
    /// <summary>
    /// Nome do índice único PARCIAL que sustenta a invariante "no máximo uma versão corrente". A regra existe
    /// no domínio, mas é o banco que a torna impossível de violar — inclusive por dois processos concorrentes,
    /// que nenhuma checagem em memória impediria.
    /// </summary>
    internal const string SingleCurrentIndexName = "ux_model_versions_single_current";

    public void Configure(EntityTypeBuilder<ModelVersion> builder)
    {
        builder.ToTable("model_versions");

        builder.HasKey(version => version.Id);
        builder.Property(version => version.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(version => version.TrainedAtUtc).IsRequired();

        builder.Property(version => version.Trainer)
            .IsRequired()
            .HasMaxLength(100);

        // Feature set como jsonb: é uma lista ordenada lida por inteiro, nunca filtrada por elemento — uma
        // tabela filha só para isso seria cerimônia sem ganho.
        builder.Ignore(version => version.Features);
        builder.Property<List<string>>("_features")
            .HasColumnName("features")
            .HasColumnType("jsonb")
            .HasConversion(
                features => JsonSerializer.Serialize(features, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null)
                        ?? new List<string>(),
                new ValueComparer<List<string>>(
                    (left, right) => left!.SequenceEqual(right!),
                    features => features.Aggregate(0, (hash, feature) => HashCode.Combine(hash, feature.GetHashCode())),
                    features => features.ToList()))
            .IsRequired();

        builder.Property(version => version.Seed).IsRequired();
        builder.Property(version => version.TestFraction).IsRequired();
        builder.Property(version => version.TrainingSampleCount).IsRequired();
        builder.Property(version => version.TestSampleCount).IsRequired();
        builder.Property(version => version.TrainedOnImputed).IsRequired();

        // O artefato do ML.NET em bytea (DP-1): o free tier tem disco efêmero, então o binário mora no mesmo
        // Postgres dos metadados — e na mesma transação, de modo que registro e artefato nunca divirjam.
        builder.Property(version => version.Artifact)
            .HasColumnName("artifact")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(version => version.ArtifactHash)
            .HasColumnName("artifact_hash")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(version => version.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Ignore(version => version.IsCurrent);

        // Métricas do modelo e do baseline como owned types em colunas próprias (não jsonb): são números
        // comparáveis entre versões, e a política de promoção compara MAE em SQL.
        builder.OwnsOne(version => version.ModelMetrics, metrics =>
        {
            metrics.Property(m => m.RSquared).HasColumnName("model_r_squared");
            metrics.Property(m => m.MeanAbsoluteError).HasColumnName("model_mae");
            metrics.Property(m => m.RootMeanSquaredError).HasColumnName("model_rmse");
        });
        builder.Navigation(version => version.ModelMetrics).IsRequired();

        builder.OwnsOne(version => version.BaselineMetrics, metrics =>
        {
            metrics.Property(m => m.RSquared).HasColumnName("baseline_r_squared");
            metrics.Property(m => m.MeanAbsoluteError).HasColumnName("baseline_mae");
            metrics.Property(m => m.RootMeanSquaredError).HasColumnName("baseline_rmse");
        });
        builder.Navigation(version => version.BaselineMetrics).IsRequired();

        // Índice único PARCIAL: só as linhas correntes participam, então pode haver muitas candidatas e no
        // máximo uma corrente. Sem o filtro, o índice permitiria apenas uma candidata no sistema inteiro.
        builder.HasIndex(version => version.Status)
            .HasDatabaseName(SingleCurrentIndexName)
            .IsUnique()
            .HasFilter($"status = '{nameof(ModelVersionStatus.Current)}'");
    }
}
