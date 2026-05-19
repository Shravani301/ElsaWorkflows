using System.Data;

namespace MozartWorkflows
{
    /// <summary>
    /// Identifies the underlying database provider for an open connection.
    /// Single source of truth — replaces the repeated inline provider-detection
    /// strings scattered across the codebase.
    /// </summary>
    public enum DbProvider
    {
        SqlServer,
        MySql,
        PostgreSql,
        Oracle,
        Unknown
    }

    /// <summary>
    /// Central repository for all SQL query strings, organised as nested static
    /// classes per database provider and domain area.
    ///
    /// Enterprise pattern (4-file partial class split):
    ///   SqlQueries.cs          — DbProvider enum, detector, resolver methods (this file)
    ///   SqlQueries.SqlServer.cs — SQL Server dialect
    ///   SqlQueries.MySql.cs     — MySQL dialect
    ///   SqlQueries.PostgreSql.cs— PostgreSQL dialect
    ///   SqlQueries.Oracle.cs    — Oracle dialect
    ///
    /// Usage:
    ///   var sql    = SqlQueries.Login(connection);
    ///   var prefix = SqlQueries.ParameterPrefix(connection);
    /// </summary>
    public static partial class SqlQueries
    {
        // ── Provider detection ────────────────────────────────────────────────

        /// <summary>
        /// Detects the database provider from a live connection's concrete type name.
        /// </summary>
        public static DbProvider DetectProvider(IDbConnection connection)
        {
            ArgumentNullException.ThrowIfNull(connection);
            var name = connection.GetType().Name.ToLowerInvariant();
            if (name.StartsWith("mysql",  StringComparison.Ordinal)) return DbProvider.MySql;
            if (name.StartsWith("npgsql", StringComparison.Ordinal)) return DbProvider.PostgreSql;
            if (name.StartsWith("oracle", StringComparison.Ordinal)) return DbProvider.Oracle;
            if (name.StartsWith("sql",    StringComparison.Ordinal)) return DbProvider.SqlServer;
            return DbProvider.Unknown;
        }

        /// <summary>
        /// Returns the parameter-prefix character for the given provider.
        /// SQL Server and Oracle use "@"; MySQL and PostgreSQL use "?".
        /// </summary>
        public static string ParameterPrefix(DbProvider provider) => provider switch
        {
            DbProvider.MySql      => "?",
            DbProvider.PostgreSql => "?",
            _                     => "@"
        };

        /// <summary>Convenience overload — detects provider and returns prefix.</summary>
        public static string ParameterPrefix(IDbConnection connection) =>
            ParameterPrefix(DetectProvider(connection));

        // ── Multi-provider resolver methods ───────────────────────────────────
        // Callers use these so they never need to switch on the provider themselves.

        /// <summary>Returns the correct login SELECT query for the connection's provider.</summary>
        public static string GetLoginQuery(IDbConnection connection) =>
            DetectProvider(connection) switch
            {
                DbProvider.MySql      => MySql.LoginUser,
                DbProvider.PostgreSql => PostgreSql.LoginUser,
                DbProvider.Oracle     => Oracle.LoginUser,
                _                     => SqlServer.LoginUser
            };

        /// <summary>Returns the correct login-audit INSERT query for the connection's provider.</summary>
        public static string GetLoginAuditInsertQuery(IDbConnection connection) =>
            DetectProvider(connection) switch
            {
                DbProvider.MySql      => MySql.InsertLoginAudit,
                DbProvider.PostgreSql => PostgreSql.InsertLoginAudit,
                DbProvider.Oracle     => Oracle.InsertLoginAudit,
                _                     => SqlServer.InsertLoginAudit
            };
    }
}
