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
/// Relatório de uma execução do enriquecimento. Distingue três desfechos por tipo — o que o card exige, para
/// a métrica não mentir:
///
/// <list type="bullet">
///   <item><b>Enriched</b>: o perfil/detalhe passou a existir nesta execução.</item>
///   <item><b>NotFound</b>: a API não retornou o id (desconhecido, removido, restrito). <b>Não é erro</b>: o
///         agregado segue pendente e será retentado até esgotar as tentativas.</item>
///   <item><b>Failed</b>: uma <b>falha real</b> ao aplicar o retorno (ex.: perfil malformado que o domínio
///         rejeita). Só isto conta como erro — antes NotFound e Failed caíam no mesmo balde.</item>
/// </list>
///
/// Segue o espírito do <c>ImportKaggleAudioFeaturesResult</c> — a métrica é o que diz o quanto o catálogo
/// avançou e onde ficaram buracos.
/// </summary>
public sealed record EnrichCatalogReferencesResult(
    int ArtistsEnriched,
    int ArtistsNotFound,
    int ArtistsFailed,
    int AlbumsEnriched,
    int AlbumsNotFound,
    int AlbumsFailed)
{
    /// <summary>Total de agregados cujo perfil passou a existir nesta execução.</summary>
    public int TotalEnriched => ArtistsEnriched + AlbumsEnriched;

    /// <summary>Total de pendentes que a API não retornou — buracos que uma próxima passada retenta.</summary>
    public int TotalNotFound => ArtistsNotFound + AlbumsNotFound;

    /// <summary>Total de falhas reais ao aplicar o retorno da API (dado inválido rejeitado pelo domínio).</summary>
    public int TotalFailed => ArtistsFailed + AlbumsFailed;
}
