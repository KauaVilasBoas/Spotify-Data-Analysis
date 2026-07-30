using SpotifyDataAnalysis.Modules.Analytics.Application.Insights;

namespace SpotifyDataAnalysis.Modules.Analytics.Tests.Application;

/// <summary>
/// Testes do CONTRATO do SQL do ranking de popularidade (E2.2). Sem Postgres vivo: afirmam, sobre o texto do
/// SQL, as invariantes dos critérios de aceite — ordenação estável, paginação por
/// <c>ROW_NUMBER() + COUNT(*) OVER()</c>, filtro opcional por gênero e extração do artista principal do jsonb
/// — e a aderência ao dialeto PostgreSQL do <see cref="BaseDataAccess"/>. A execução real contra o schema
/// fica para o smoke test com banco (Docker), sinalizado como posterior.
/// </summary>
public sealed class GetPopularityRankingSqlTests
{
    private static readonly string Sql = GetPopularityRankingQueryHandler.Sql;

    [Fact]
    public void Sql_OrdersByPopularityDescending_WithDeterministicTieBreakOnId()
    {
        // Ordenação estável: sem o desempate por id, faixas de mesma popularity poderiam trocar de posição
        // entre round-trips e reaparecer/sumir entre páginas.
        Assert.Contains("ORDER BY t.popularity DESC, t.id ASC", Sql);
    }

    [Fact]
    public void Sql_PaginatesWithRowNumberAndCountOver_SingleRoundTrip()
    {
        // ROW_NUMBER() recorta a página; COUNT(*) OVER() traz o total na MESMA consulta (1 round-trip).
        Assert.Contains("ROW_NUMBER() OVER (ORDER BY t.popularity DESC, t.id ASC)", Sql);
        Assert.Contains("COUNT(*)     OVER ()", Sql);
        Assert.Contains("WHERE row_number BETWEEN @FirstResult AND @LastResult", Sql);
    }

    [Fact]
    public void Sql_GenreFilter_IsOptional_AndReadsFromAudioFeaturesJsonb()
    {
        // Filtro opcional (DP-1): sem @Genre o predicado é verdadeiro para todas as faixas (inclui as sem
        // features); com @Genre restringe pelo gênero do jsonb, excluindo as de audio_features nulo.
        Assert.Contains("(@Genre IS NULL OR t.audio_features ->> 'Genre' = @Genre)", Sql);
    }

    [Fact]
    public void Sql_ExtractsPrimaryArtist_FromFirstElementOfArtistsJsonbArray()
    {
        // Artista principal = 1º crédito do array jsonb "artists" (a ordem preserva a da API do Spotify).
        Assert.Contains("t.artists -> 0 ->> 'Name'", Sql);
    }

    [Fact]
    public void Sql_SelectsGenre_FromAudioFeaturesJsonb()
    {
        Assert.Contains("t.audio_features ->> 'Genre'", Sql);
    }

    [Fact]
    public void Sql_ReadsPopularity_FromItsOwnColumn_NotFromJsonb()
    {
        // popularity é coluna própria (int) — não deve ser lida do jsonb.
        Assert.Contains("t.popularity", Sql);
        Assert.DoesNotContain("->> 'Popularity'", Sql);
    }

    [Fact]
    public void Sql_FollowsPostgresConventions_NoSqlServerDialect()
    {
        Assert.DoesNotContain("WITH(NOLOCK)", Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NOLOCK", Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[", Sql); // sem identificadores entre colchetes (T-SQL)
        Assert.DoesNotContain("TOP ", Sql, StringComparison.OrdinalIgnoreCase); // paginação por ROW_NUMBER, não TOP
    }
}
