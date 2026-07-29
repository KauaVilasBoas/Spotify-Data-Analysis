using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Enriquece os artistas e álbuns que entraram no catálogo apenas como <b>referência</b> (id + nome, vindos
/// embutidos nas faixas de uma playlist) com o perfil completo dos endpoints <c>/artists</c> e <c>/albums</c>
/// da API — popularidade, seguidores e gêneros do artista; data de lançamento e total de faixas do álbum
/// (E1.8). É um command (muda estado): o SaveChanges + Outbox são disparados pelo TransactionBehavior/UnitOfWork.
///
/// <para>Processa no máximo <see cref="BatchSize"/> artistas e <see cref="BatchSize"/> álbuns por execução —
/// os que ainda têm <c>IsEnriched == false</c>. Uma execução é uma fatia do trabalho, não a fila inteira: o
/// job agendado (ou reexecuções) drena o restante, mantendo cada transação curta e o consumo de rate limit
/// previsível. Rodar de novo é <b>idempotente</b> — os já enriquecidos deixam de ser candidatos e nenhum
/// agregado é duplicado.</para>
/// </summary>
public sealed record EnrichCatalogReferencesCommand(int BatchSize = EnrichCatalogReferencesCommand.DefaultBatchSize)
    : ICommand<EnrichCatalogReferencesResult>
{
    /// <summary>
    /// Tamanho de lote padrão. Escolhido como múltiplo dos tetos dos endpoints em lote da API (50 artistas /
    /// 20 álbuns por chamada), para varrer um bom volume por execução sem transação longa demais.
    /// </summary>
    public const int DefaultBatchSize = 200;
}

/// <summary>
/// Relatório de uma execução do enriquecimento: quantos artistas e álbuns foram efetivamente enriquecidos e
/// quantos, embora pendentes, a API não retornou (id desconhecido, recurso removido) e por isso continuam
/// para a próxima passada. Segue o espírito do <c>ImportKaggleAudioFeaturesResult</c> — a métrica é o que diz
/// o quanto o catálogo avançou e onde ficaram buracos.
/// </summary>
public sealed record EnrichCatalogReferencesResult(
    int ArtistsEnriched,
    int ArtistsFailed,
    int AlbumsEnriched,
    int AlbumsFailed)
{
    /// <summary>Total de agregados cujo perfil passou a existir nesta execução.</summary>
    public int TotalEnriched => ArtistsEnriched + AlbumsEnriched;

    /// <summary>Total de pendentes tentados mas não resolvidos — o resíduo que uma próxima passada retenta.</summary>
    public int TotalFailed => ArtistsFailed + AlbumsFailed;
}
