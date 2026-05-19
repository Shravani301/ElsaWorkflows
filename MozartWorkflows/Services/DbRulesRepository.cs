using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MozartWorkflows.Services.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Threading.Tasks;

namespace MozartWorkflows.Services
{
    /// <summary>
    /// Repository for rules artifacts with provider-aware SQL.
    /// DB provider is read from configuration key: "DatabaseProvider" = "SqlServer" | "Postgres" | "MySql".
    /// Focuses on low-latency reads (indexes recommended:
    ///   RE_RuleSetJson(ApplicationId, WorkflowName), CaseData(CaseId), RE_Parameter(IsStandard)).
    /// </summary>
    public sealed class DbRulesRepository : IRulesRepository
    {
        private const string SqlServerProvider = "SqlServer";
        private const string MySqlProvider = "MySql";
        private const string PostgresProvider = "Postgres";
        private readonly IDbConnectionFactory _dbConnectionFactory;
        private readonly ILogger<DbRulesRepository> _logger;
        private readonly string _provider; // "SqlServer" | "Postgres" | "MySql"
        private readonly string _paramPrefix; // @ for SqlServer, ? for Postgres/MySql

        // Keep commands snappy under load; adjust per env if needed.
        private const int DefaultCommandTimeoutSeconds = 5;

        public DbRulesRepository(
            IDbConnectionFactory dbConnectionFactory,
            ILogger<DbRulesRepository> logger,
            IConfiguration configuration)
        {
            _dbConnectionFactory = dbConnectionFactory;
            _logger = logger;

            _provider = configuration["DatabaseProvider"]?.Trim() ?? SqlServerProvider;
            _paramPrefix = _provider switch
            {
                MySqlProvider => "?",
                PostgresProvider => "?",
                SqlServerProvider => "@",
                _ => throw new NotSupportedException($"Unsupported DatabaseProvider '{_provider}'. Use SqlServer | Postgres | MySql.")
            };
        }

        /// <summary>
        /// Load rules JSON & its RuleSetJsonId for a given (ApplicationId, WorkflowName).
        /// Uses SingleRow + ordinal reads for speed.
        /// </summary>
        public async Task<(string rulesJson, int ruleSetJsonId)?> GetRulesJsonAsync(int applicationId, string workflowName)
        {
            try
            {
                using var connection = _dbConnectionFactory.CreateConnection();
                await OpenAsync(connection).ConfigureAwait(false);

                var sql = _provider switch
                {
                    MySqlProvider or PostgresProvider =>
                        $"SELECT RulesJson, RuleSetJsonId " +
                        $"FROM RE_RuleSetJson " +
                        $"WHERE ApplicationId = {_paramPrefix}ApplicationId AND WorkflowName = {_paramPrefix}WorkflowName " +
                        $"LIMIT 1",
                    SqlServerProvider =>
                        $"SELECT TOP (1) RulesJson, RuleSetJsonId " +
                        $"FROM RE_RuleSetJson " +
                        $"WHERE ApplicationId = {_paramPrefix}ApplicationId AND WorkflowName = {_paramPrefix}WorkflowName",
                    _ => throw new NotSupportedException($"Unsupported DatabaseProvider '{_provider}'.")
                };

                using var cmd = CreateCommand(connection, sql,
                    ("ApplicationId", DbType.Int32, applicationId),
                    ("WorkflowName", DbType.String, workflowName));

                _logger.LogDebug("GetRulesJsonAsync SQL ({Provider}): {Sql}", _provider, sql);

                if (cmd is DbCommand dbc)
                {
                    dbc.CommandTimeout = DefaultCommandTimeoutSeconds;
                    using var reader = await dbc.ExecuteReaderAsync(
                        CommandBehavior.SingleRow | CommandBehavior.SequentialAccess | CommandBehavior.CloseConnection
                    ).ConfigureAwait(false);

                    if (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        // Ordinal reads (SELECT RulesJson, RuleSetJsonId ...)
                        var rulesJson = reader.GetString(0);
                        var ruleSetJsonId = reader.GetInt32(1);
                        return (rulesJson, ruleSetJsonId);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetRulesJsonAsync failed. AppId={AppId}, Workflow={Workflow}", applicationId, workflowName);
                return null;
            }
        }

        /// <summary>
        /// Fetch the raw FormData JSON for a CaseId.
        /// </summary>
        public async Task<string?> GetCaseDataJsonAsync(string caseId)
        {
            try
            {
                using var connection = _dbConnectionFactory.CreateConnection();
                await OpenAsync(connection).ConfigureAwait(false);

                var sql = _provider switch
                {
                    MySqlProvider or PostgresProvider =>
                        $"SELECT FormData FROM CaseData WHERE CaseId = {_paramPrefix}CaseId LIMIT 1",
                    SqlServerProvider =>
                        $"SELECT TOP (1) FormData FROM CaseData WHERE CaseId = {_paramPrefix}CaseId",
                    _ => throw new NotSupportedException($"Unsupported DatabaseProvider '{_provider}'.")
                };

                using var cmd = CreateCommand(connection, sql, ("CaseId", DbType.String, caseId));

                _logger.LogDebug("GetCaseDataJsonAsync SQL ({Provider}): {Sql}", _provider, sql);

                if (cmd is DbCommand dbc)
                {
                    dbc.CommandTimeout = DefaultCommandTimeoutSeconds;
                    using var reader = await dbc.ExecuteReaderAsync(
                        CommandBehavior.SingleRow | CommandBehavior.SequentialAccess | CommandBehavior.CloseConnection
                    ).ConfigureAwait(false);

                    if (await reader.ReadAsync().ConfigureAwait(false))
                        return await reader.IsDBNullAsync(0).ConfigureAwait(false) ? null : reader.GetString(0);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCaseDataJsonAsync failed. CaseId={CaseId}", caseId);
                return null;
            }
        }

        /// <summary>
        /// Per-RuleSet defaults (kept for compatibility). If you’re moving to global defaults,
        /// prefer GetAllDefaultValuesAsync + cache the parsed dictionary once in your service.
        /// </summary>
        public async Task<string?> GetDefaultValuesAsync(int ruleSetJsonId)
        {
            try
            {
                using var connection = _dbConnectionFactory.CreateConnection();
                await OpenAsync(connection).ConfigureAwait(false);

                var sql = _provider switch
                {
                    MySqlProvider or PostgresProvider =>
                        $"SELECT p.ParameterCode, p.Value " +
                        $"FROM RE_Parameter p " +
                        $"INNER JOIN RE_RuleSetParameterMap r ON p.ParameterId = r.ParameterId " +
                        $"WHERE r.RuleSetJsonId = {_paramPrefix}RuleSetJsonId AND p.IsStandard = 1",
                    SqlServerProvider =>
                        $"SELECT p.ParameterCode, p.Value " +
                        $"FROM dbo.RE_Parameter p " +
                        $"INNER JOIN dbo.RE_RuleSetParameterMap r ON p.ParameterId = r.ParameterId " +
                        $"WHERE r.RuleSetJsonId = {_paramPrefix}RuleSetJsonId AND p.IsStandard = 1",
                    _ => throw new NotSupportedException($"Unsupported DatabaseProvider '{_provider}'.")
                };

                using var cmd = CreateCommand(connection, sql, ("RuleSetJsonId", DbType.Int32, ruleSetJsonId));

                var dict = await ExecuteDefaultValuesReaderAsync(cmd).ConfigureAwait(false);
                return JsonConvert.SerializeObject(dict ?? new Dictionary<string, object>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetDefaultValuesAsync failed. RuleSetJsonId={RuleSetJsonId}", ruleSetJsonId);
                return null;
            }
        }

        /// <summary>
        /// Global defaults (no RuleSetJsonId filter).
        /// Call this ONCE and cache the parsed dictionary in your service for maximum performance.
        /// </summary>
        public async Task<string?> GetAllDefaultValuesAsync()
        {
            try
            {
                using var connection = _dbConnectionFactory.CreateConnection();
                await OpenAsync(connection).ConfigureAwait(false);

                var sql = _provider switch
                {
                    MySqlProvider or PostgresProvider =>
                        "SELECT p.ParameterCode, p.Value FROM RE_Parameter p WHERE p.IsStandard = 1",
                    SqlServerProvider =>
                        "SELECT p.ParameterCode, p.Value FROM dbo.RE_Parameter p WHERE p.IsStandard = 1",
                    _ => throw new NotSupportedException($"Unsupported DatabaseProvider '{_provider}'.")
                };

                using var cmd = CreateCommand(connection, sql);

                var dict = await ExecuteDefaultValuesReaderAsync(cmd).ConfigureAwait(false);
                return JsonConvert.SerializeObject(dict ?? new Dictionary<string, object>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAllDefaultValuesAsync failed.");
                return null;
            }
        }

        #region Internal helpers

        /// <summary>
        /// Create a provider-neutral command with parameters.
        /// Uses the configured parameter prefix for the current provider.
        /// </summary>
        private IDbCommand CreateCommand(IDbConnection connection, string sql,
            params (string Name, DbType Type, object? Value)[] parameters)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;

            if (parameters is { Length: > 0 })
            {
                foreach (var (Name, Type, Value) in parameters)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = $"{_paramPrefix}{Name}";
                    p.DbType = Type;
                    p.Value = Value ?? DBNull.Value;
                    cmd.Parameters.Add(p);
                }
            }

            return cmd;
        }

        /// <summary>
        /// Read defaults rows into a case-insensitive dictionary with minimal allocations.
        /// </summary>
        private static async Task<Dictionary<string, object>?> ExecuteDefaultValuesReaderAsync(IDbCommand cmd)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (cmd is DbCommand dbc)
            {
                dbc.CommandTimeout = DefaultCommandTimeoutSeconds;
                using var reader = await dbc.ExecuteReaderAsync(
                    CommandBehavior.SingleResult | CommandBehavior.SequentialAccess | CommandBehavior.CloseConnection
                ).ConfigureAwait(false);

                // Ordinal access (SELECT ParameterCode, Value)
                int codeOrd = 0, valOrd = 1;

                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    if (await reader.IsDBNullAsync(codeOrd).ConfigureAwait(false)) continue;
                    var code = reader.GetString(codeOrd);

                    object value = DBNull.Value;
                    if (!await reader.IsDBNullAsync(valOrd).ConfigureAwait(false))
                    {
                        var raw = reader.GetString(valOrd);
                        value = ParseValue(raw);
                    }

                    dict[code] = value;
                }
            }

            return dict.Count > 0 ? dict : null;
        }

        /// <summary>
        /// Fast, culture-invariant parsing to common primitives; falls back to string.
        /// </summary>
        private static object ParseValue(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return d;
            if (bool.TryParse(raw, out var b)) return b;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)) return dt;

            return raw;
        }

        /// <summary>
        /// Opens the connection using async when possible.
        /// </summary>
        private static async Task OpenAsync(IDbConnection connection)
        {
            if (connection is DbConnection dbc)
                await dbc.OpenAsync().ConfigureAwait(false);
            else
                connection.Open();
        }

        #endregion
    }
}
