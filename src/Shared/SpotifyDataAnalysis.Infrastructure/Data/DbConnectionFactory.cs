using System.Data;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace SpotifyDataAnalysis.Infrastructure.Data;

/// <summary>
/// Npgsql connection factory for the Dapper read-side.
/// Register as Scoped — one instance per HTTP request.
///
/// The connection is only opened when a Dapper handler calls CreateOpenConnectionAsync.
/// If PostgreSQL is unavailable, the health check reports unhealthy but does not block startup.
/// </summary>
public sealed class DbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("SpotifyDb")
            ?? throw new InvalidOperationException(
                "Connection string 'SpotifyDb' not found. " +
                "Configure it in appsettings.json or via ConnectionStrings__SpotifyDb environment variable.");
    }

    /// <summary>
    /// Creates and opens a new Npgsql connection. The caller is responsible for disposing it.
    /// </summary>
    public async Task<IDbConnection> CreateOpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
