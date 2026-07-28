using System.Data;

namespace SpotifyDataAnalysis.Infrastructure.Data;

/// <summary>
/// Base class for read-side query handlers (Dapper). Project convention:
/// the Query, QueryHandler, Result, and ResultItem are all in the SAME .cs file; the handler
/// inherits this class and gets the connection via <see cref="OpenConnectionAsync"/>.
///
/// SQL dialect (PostgreSQL — NOT SQL Server):
/// <code>
/// WITH records AS (
///     SELECT t.id,
///            t.name,
///            ROW_NUMBER() OVER (ORDER BY t.name ASC) AS row_number,
///            COUNT(*)     OVER ()                     AS total_rows
///     FROM catalog.track AS t
///     WHERE (@Search IS NULL OR t.name ILIKE '%' || @Search || '%')
/// )
/// SELECT * FROM records
/// WHERE row_number BETWEEN @FirstResult AND @LastResult
/// ORDER BY row_number;
/// </code>
///
/// Rules:
/// - Identifiers in lowercase / double-quoted (never square brackets <c>[...]</c>).
/// - <c>ILIKE</c> instead of <c>LIKE</c>; concatenation with <c>||</c>.
/// - No <c>WITH(NOLOCK)</c> (does not exist in PostgreSQL).
/// - Pagination via <c>ROW_NUMBER()</c> + <c>COUNT(*) OVER()</c> using
///   <c>PagedQuery.FirstResult/LastResult</c>.
/// </summary>
public abstract class BaseDataAccess
{
    private readonly DbConnectionFactory _connectionFactory;

    protected BaseDataAccess(DbConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    /// <summary>
    /// Opens a new Npgsql connection for the Dapper query.
    /// The caller is responsible for disposing the connection (use <c>using</c>).
    /// </summary>
    protected Task<IDbConnection> OpenConnectionAsync()
        => _connectionFactory.CreateOpenConnectionAsync();
}
