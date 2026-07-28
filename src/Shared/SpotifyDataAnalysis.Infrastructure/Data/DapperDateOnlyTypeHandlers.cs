using System.Data;
using Dapper;

namespace SpotifyDataAnalysis.Infrastructure.Data;

/// <summary>
/// Dapper type handlers that teach the read-side to bind <see cref="DateOnly"/> and <see cref="TimeOnly"/>
/// as query parameters and to materialize them from result columns. Dapper 2.1.x does not map these types
/// out of the box — an unregistered <see cref="DateOnly"/> parameter throws
/// <see cref="NotSupportedException"/> before Npgsql is ever reached. Any read-side query that passes a
/// <see cref="DateOnly"/> parameter or projects a <c>date</c> column back to <see cref="DateOnly"/> needs
/// these handlers for the Dapper path to work against PostgreSQL.
/// </summary>
/// <remarks>
/// Registered once at composition (see <c>AddSpotifyInfrastructure</c>). Npgsql maps a
/// <see cref="DateTime"/> with <see cref="DbType.Date"/> to a PostgreSQL <c>date</c> and a
/// <see cref="TimeSpan"/> to <c>time</c>, so each handler converts to/from those CLR types.
/// </remarks>
public static class DapperDateOnlyTypeHandlers
{
    private static bool _registered;
    private static readonly object Gate = new();

    /// <summary>Registers the <see cref="DateOnly"/>/<see cref="TimeOnly"/> handlers once (idempotent, thread-safe).</summary>
    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
            SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());
            _registered = true;
        }
    }

    /// <summary>Binds <see cref="DateOnly"/> as a <c>date</c> parameter and reads it back from a date/timestamp column.</summary>
    private sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        }

        public override DateOnly Parse(object value) => value switch
        {
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            DateOnly dateOnly => dateOnly,
            _ => DateOnly.FromDateTime(Convert.ToDateTime(value))
        };
    }

    /// <summary>Binds <see cref="TimeOnly"/> as a <c>time</c> parameter and reads it back from a time column.</summary>
    private sealed class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
    {
        public override void SetValue(IDbDataParameter parameter, TimeOnly value)
        {
            parameter.DbType = DbType.Time;
            parameter.Value = value.ToTimeSpan();
        }

        public override TimeOnly Parse(object value) => value switch
        {
            TimeSpan timeSpan => TimeOnly.FromTimeSpan(timeSpan),
            TimeOnly timeOnly => timeOnly,
            DateTime dateTime => TimeOnly.FromDateTime(dateTime),
            _ => TimeOnly.FromTimeSpan((TimeSpan)value)
        };
    }
}
