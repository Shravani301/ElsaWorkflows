using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using MozartWorkflows.Models;
using MozartWorkflows.Services.Interfaces;
using MySqlConnector;
using Npgsql;
using System.Data;
using System.Diagnostics;

namespace MozartWorkflows.Services
{
    public class AuditServiceImpl : IAuditService
    {
        private const string SqlServerProvider = "SqlServer";
        private const string MySqlProvider = "MySql";
        private const string PostgreSqlProvider = "PostgreSql";
        private const string PostgresProvider = "Postgres";
        private readonly IDbConnectionFactory _dbContext;
        private readonly IConfiguration _config;

        public AuditServiceImpl(IDbConnectionFactory connectionFactory, IConfiguration config)
        {
            _dbContext = connectionFactory;
            _config = config;
        }

        private string Provider => _config["DatabaseProvider"] ?? SqlServerProvider;

        private const string RuleAuditTable = "RuleExecutionAudit";
        private const string WorkflowAuditTable = "WorkflowExecutionAudit";

        private const string ColWorkflowAuditId = "WorkflowAuditId";
        private const string ColRuleName = "RuleName";
        private const string ColStartTime = "StartTime";
        private const string ColEndTime = "EndTime";
        private const string ColIsSuccess = "IsSuccess";
        private const string ColExceptionMessage = "ExceptionMessage";
        private const string ColGlobalVariableValue = "GlobalVariableValue";
        private const string ColSequenceNo = "SequenceNo";
        private const string ColWorkflowName = "WorkflowName";

        private static string Trunc(string? value, int max)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.Length <= max)
                return value;

            return value.Substring(0, max);
        }

        public async Task BulkInsertRuleAuditsAsync(IEnumerable<RuleExecutionAudit> audits)
        {
            var list = audits?.ToList();
            if (list == null || list.Count == 0)
                return;

            var totalSw = Stopwatch.StartNew();

            switch (Provider)
            {
                case string provider when provider.Equals(SqlServerProvider, StringComparison.OrdinalIgnoreCase):
                    await BulkInsertRuleAuditsSqlServerAsync(list, totalSw);
                    return;
                case string provider when provider.Equals(MySqlProvider, StringComparison.OrdinalIgnoreCase):
                    await BulkInsertRuleAuditsMySqlAsync(list, totalSw);
                    return;
                case string provider when provider.Equals(PostgreSqlProvider, StringComparison.OrdinalIgnoreCase)
                    || provider.Equals(PostgresProvider, StringComparison.OrdinalIgnoreCase):
                    await BulkInsertRuleAuditsPostgresAsync(list, totalSw);
                    return;
                default:
                    throw new NotSupportedException($"DatabaseProvider '{Provider}' is not supported by AuditServiceImpl bulk insert.");
            }
        }

        public async Task BulkInsertWorkflowAuditsAsync(IEnumerable<WorkflowExecutionAudit> audits)
        {
            var list = audits?.ToList();
            if (list == null || list.Count == 0)
                return;

            var totalSw = Stopwatch.StartNew();

            switch (Provider)
            {
                case string provider when provider.Equals(SqlServerProvider, StringComparison.OrdinalIgnoreCase):
                    await BulkInsertWorkflowAuditsSqlServerAsync(list, totalSw);
                    return;
                case string provider when provider.Equals(MySqlProvider, StringComparison.OrdinalIgnoreCase):
                    await BulkInsertWorkflowAuditsMySqlAsync(list, totalSw);
                    return;
                case string provider when provider.Equals(PostgreSqlProvider, StringComparison.OrdinalIgnoreCase)
                    || provider.Equals(PostgresProvider, StringComparison.OrdinalIgnoreCase):
                    await BulkInsertWorkflowAuditsPostgresAsync(list, totalSw);
                    return;
                default:
                    throw new NotSupportedException($"DatabaseProvider '{Provider}' is not supported by AuditServiceImpl bulk insert.");
            }
        }

        public Task BulkUpdateWorkflowAuditsAsync(IEnumerable<WorkflowExecutionAudit> audits)
            => throw new NotImplementedException("BulkUpdateWorkflowAuditsAsync not implemented in the bulk-insert variant.");

        private async Task BulkInsertRuleAuditsSqlServerAsync(IReadOnlyList<RuleExecutionAudit> audits, Stopwatch totalSw)
        {
            await using var connection = (SqlConnection)_dbContext.CreateConnection();
            await connection.OpenAsync();

            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
            var bulkSw = Stopwatch.StartNew();

            using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, tx))
            {
                bulk.DestinationTableName = RuleAuditTable;
                bulk.BulkCopyTimeout = 0;
                bulk.EnableStreaming = true;

                bulk.ColumnMappings.Add(ColWorkflowAuditId, ColWorkflowAuditId);
                bulk.ColumnMappings.Add(ColRuleName, ColRuleName);
                bulk.ColumnMappings.Add(ColStartTime, ColStartTime);
                bulk.ColumnMappings.Add(ColEndTime, ColEndTime);
                bulk.ColumnMappings.Add(ColIsSuccess, ColIsSuccess);
                bulk.ColumnMappings.Add(ColExceptionMessage, ColExceptionMessage);
                bulk.ColumnMappings.Add(ColGlobalVariableValue, ColGlobalVariableValue);
                bulk.ColumnMappings.Add(ColSequenceNo, ColSequenceNo);

                using var reader = new RuleAuditDataReader(audits);
                await bulk.WriteToServerAsync(reader);
            }

            bulkSw.Stop();
            await tx.CommitAsync();
            LogBulkInsertDuration(SqlServerProvider, "RuleAudit", audits.Count, totalSw, bulkSw);
        }

        private async Task BulkInsertRuleAuditsMySqlAsync(IReadOnlyList<RuleExecutionAudit> audits, Stopwatch totalSw)
        {
            await using var connection = (MySqlConnection)_dbContext.CreateConnection();
            await connection.OpenAsync();

            var bulkSw = Stopwatch.StartNew();
            var bulk = new MySqlBulkCopy(connection)
            {
                DestinationTableName = RuleAuditTable,
                BulkCopyTimeout = 0
            };

            bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping { SourceOrdinal = 0, DestinationColumn = ColWorkflowAuditId });
            bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping { SourceOrdinal = 1, DestinationColumn = ColRuleName });
            bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping { SourceOrdinal = 2, DestinationColumn = ColStartTime });
            bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping { SourceOrdinal = 3, DestinationColumn = ColEndTime });
            bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping { SourceOrdinal = 4, DestinationColumn = ColIsSuccess });
            bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping { SourceOrdinal = 5, DestinationColumn = ColExceptionMessage });
            bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping { SourceOrdinal = 6, DestinationColumn = ColGlobalVariableValue });
            bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping { SourceOrdinal = 7, DestinationColumn = ColSequenceNo });

            using var reader = new RuleAuditDataReader(audits);
            await bulk.WriteToServerAsync(reader);

            bulkSw.Stop();
            LogBulkInsertDuration(MySqlProvider, "RuleAudit", audits.Count, totalSw, bulkSw);
        }

        private async Task BulkInsertRuleAuditsPostgresAsync(IReadOnlyList<RuleExecutionAudit> audits, Stopwatch totalSw)
        {
            await using var connection = (NpgsqlConnection)_dbContext.CreateConnection();
            await connection.OpenAsync();

            var copySql =
                $"COPY \"{RuleAuditTable}\" " +
                $"(\"{ColWorkflowAuditId}\",\"{ColRuleName}\",\"{ColStartTime}\",\"{ColEndTime}\",\"{ColIsSuccess}\",\"{ColExceptionMessage}\",\"{ColGlobalVariableValue}\",\"{ColSequenceNo}\") " +
                "FROM STDIN (FORMAT BINARY)";

            var bulkSw = Stopwatch.StartNew();

            await using (var importer = await connection.BeginBinaryImportAsync(copySql))
            {
                foreach (var audit in audits)
                {
                    await importer.StartRowAsync();
                    await importer.WriteAsync(Trunc(audit.WorkflowAuditId, 50));
                    await importer.WriteAsync(Trunc(audit.RuleName, 200));
                    await importer.WriteAsync(audit.StartTime);
                    await importer.WriteAsync(audit.EndTime);
                    await importer.WriteAsync(audit.IsSuccess);
                    await WriteNullableStringAsync(importer, audit.ExceptionMessage, 2000);
                    await WriteNullableStringAsync(importer, audit.GlobalVariableValue, 100);
                    await importer.WriteAsync(audit.SequenceNo);
                }

                await importer.CompleteAsync();
            }

            bulkSw.Stop();
            LogBulkInsertDuration(PostgreSqlProvider, "RuleAudit", audits.Count, totalSw, bulkSw);
        }

        private async Task BulkInsertWorkflowAuditsSqlServerAsync(IReadOnlyList<WorkflowExecutionAudit> audits, Stopwatch totalSw)
        {
            await using var connection = (SqlConnection)_dbContext.CreateConnection();
            await connection.OpenAsync();

            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
            var bulkSw = Stopwatch.StartNew();

            using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, tx))
            {
                bulk.DestinationTableName = WorkflowAuditTable;
                bulk.BulkCopyTimeout = 0;
                bulk.EnableStreaming = true;

                bulk.ColumnMappings.Add(ColWorkflowAuditId, ColWorkflowAuditId);
                bulk.ColumnMappings.Add(ColWorkflowName, ColWorkflowName);
                bulk.ColumnMappings.Add(ColStartTime, ColStartTime);
                bulk.ColumnMappings.Add(ColEndTime, ColEndTime);
                bulk.ColumnMappings.Add(ColIsSuccess, ColIsSuccess);
                bulk.ColumnMappings.Add(ColExceptionMessage, ColExceptionMessage);

                using var reader = new WorkflowAuditDataReader(audits);
                await bulk.WriteToServerAsync(reader);
            }

            bulkSw.Stop();
            await tx.CommitAsync();
            LogBulkInsertDuration(SqlServerProvider, "WorkflowAudit", audits.Count, totalSw, bulkSw);
        }

        private async Task BulkInsertWorkflowAuditsMySqlAsync(IReadOnlyList<WorkflowExecutionAudit> audits, Stopwatch totalSw)
        {
            await using var connection = (MySqlConnection)_dbContext.CreateConnection();
            await connection.OpenAsync();

            var bulkSw = Stopwatch.StartNew();
            var bulk = new MySqlBulkCopy(connection)
            {
                DestinationTableName = WorkflowAuditTable,
                BulkCopyTimeout = 0
            };

            bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping { SourceOrdinal = 0, DestinationColumn = ColWorkflowAuditId });
            bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping { SourceOrdinal = 1, DestinationColumn = ColWorkflowName });
            bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping { SourceOrdinal = 2, DestinationColumn = ColStartTime });
            bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping { SourceOrdinal = 3, DestinationColumn = ColEndTime });
            bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping { SourceOrdinal = 4, DestinationColumn = ColIsSuccess });
            bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping { SourceOrdinal = 5, DestinationColumn = ColExceptionMessage });

            using var reader = new WorkflowAuditDataReader(audits);
            await bulk.WriteToServerAsync(reader);

            bulkSw.Stop();
            LogBulkInsertDuration(MySqlProvider, "WorkflowAudit", audits.Count, totalSw, bulkSw);
        }

        private async Task BulkInsertWorkflowAuditsPostgresAsync(IReadOnlyList<WorkflowExecutionAudit> audits, Stopwatch totalSw)
        {
            await using var connection = (NpgsqlConnection)_dbContext.CreateConnection();
            await connection.OpenAsync();

            var copySql =
                $"COPY \"{WorkflowAuditTable}\" " +
                $"(\"{ColWorkflowAuditId}\",\"{ColWorkflowName}\",\"{ColStartTime}\",\"{ColEndTime}\",\"{ColIsSuccess}\",\"{ColExceptionMessage}\") " +
                "FROM STDIN (FORMAT BINARY)";

            var bulkSw = Stopwatch.StartNew();

            await using (var importer = await connection.BeginBinaryImportAsync(copySql))
            {
                foreach (var audit in audits)
                {
                    await importer.StartRowAsync();
                    await importer.WriteAsync(Trunc(audit.WorkflowAuditId, 50));
                    await importer.WriteAsync(Trunc(audit.WorkflowName, 200));
                    await importer.WriteAsync(audit.StartTime);
                    await importer.WriteAsync(audit.EndTime);
                    await importer.WriteAsync(audit.IsSuccess);
                    await WriteNullableStringAsync(importer, audit.ExceptionMessage, 2000);
                }

                await importer.CompleteAsync();
            }

            bulkSw.Stop();
            LogBulkInsertDuration(PostgreSqlProvider, "WorkflowAudit", audits.Count, totalSw, bulkSw);
        }

        private static async Task WriteNullableStringAsync(NpgsqlBinaryImporter importer, string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                await importer.WriteNullAsync();
                return;
            }

            await importer.WriteAsync(Trunc(value, maxLength));
        }

        private static void LogBulkInsertDuration(string provider, string operation, int count, Stopwatch totalSw, Stopwatch bulkSw)
        {
            totalSw.Stop();
            Console.WriteLine($"[AuditService][{provider}] {operation}: {count} in {totalSw.ElapsedMilliseconds}ms | Bulk: {bulkSw.ElapsedMilliseconds}ms");
        }

        private sealed class RuleAuditDataReader : IDataReader
        {
            private static readonly string[] Cols =
            {
                "WorkflowAuditId","RuleName","StartTime","EndTime","IsSuccess","ExceptionMessage","GlobalVariableValue","SequenceNo"
            };

            private readonly IReadOnlyList<RuleExecutionAudit> _audits;
            private int _currentIndex = -1;

            public RuleAuditDataReader(IReadOnlyList<RuleExecutionAudit> audits) => _audits = audits;

            public bool Read()
            {
                _currentIndex++;
                return _currentIndex < _audits.Count;
            }

            public object GetValue(int i)
            {
                var a = _audits[_currentIndex];
                return i switch
                {
                    0 => Trunc(a.WorkflowAuditId, 50),
                    1 => Trunc(a.RuleName, 200),
                    2 => a.StartTime,
                    3 => a.EndTime,
                    4 => a.IsSuccess,
                    5 => string.IsNullOrWhiteSpace(a.ExceptionMessage) ? DBNull.Value : Trunc(a.ExceptionMessage, 2000),
                    6 => string.IsNullOrWhiteSpace(a.GlobalVariableValue) ? DBNull.Value : Trunc(a.GlobalVariableValue, 100),
                    7 => a.SequenceNo,
                    _ => throw new ArgumentOutOfRangeException(nameof(i), i, "Ordinal out of range.")
                };
            }

            public int FieldCount => Cols.Length;
            public string GetName(int i) => Cols[i];
            public int GetOrdinal(string name) => Array.IndexOf(Cols, name);
            public Type GetFieldType(int i) => typeof(object);
            public string GetDataTypeName(int i) => "variant";
            public bool IsDBNull(int i) => GetValue(i) == DBNull.Value;
            public object this[int i] => GetValue(i);
            public object this[string name] => GetValue(GetOrdinal(name));
            public int Depth => 0;
            public bool IsClosed => false;
            public int RecordsAffected => -1;

            public void Dispose() { }
            public bool NextResult() => false;
            public void Close() { }
            public DataTable GetSchemaTable() => null!;
            public int GetValues(object[] values) => throw new NotImplementedException();
            public bool GetBoolean(int i) => (bool)GetValue(i);
            public byte GetByte(int i) => (byte)GetValue(i);
            public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
            public char GetChar(int i) => (char)GetValue(i);
            public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
            public Guid GetGuid(int i) => (Guid)GetValue(i);
            public short GetInt16(int i) => Convert.ToInt16(GetValue(i));
            public int GetInt32(int i) => Convert.ToInt32(GetValue(i));
            public long GetInt64(int i) => Convert.ToInt64(GetValue(i));
            public float GetFloat(int i) => Convert.ToSingle(GetValue(i));
            public double GetDouble(int i) => Convert.ToDouble(GetValue(i));
            public string GetString(int i) => Convert.ToString(GetValue(i))!;
            public decimal GetDecimal(int i) => Convert.ToDecimal(GetValue(i));
            public DateTime GetDateTime(int i) => Convert.ToDateTime(GetValue(i));
            public IDataReader GetData(int i) => throw new NotImplementedException();
        }

        private sealed class WorkflowAuditDataReader : IDataReader
        {
            private static readonly string[] Cols =
            {
                "WorkflowAuditId","WorkflowName","StartTime","EndTime","IsSuccess","ExceptionMessage"
            };

            private readonly IReadOnlyList<WorkflowExecutionAudit> _audits;
            private int _currentIndex = -1;

            public WorkflowAuditDataReader(IReadOnlyList<WorkflowExecutionAudit> audits) => _audits = audits;

            public bool Read()
            {
                _currentIndex++;
                return _currentIndex < _audits.Count;
            }

            public object GetValue(int i)
            {
                var a = _audits[_currentIndex];
                return i switch
                {
                    0 => Trunc(a.WorkflowAuditId, 50),
                    1 => Trunc(a.WorkflowName, 200),
                    2 => a.StartTime,
                    3 => (object?)a.EndTime ?? DBNull.Value,
                    4 => a.IsSuccess,
                    5 => string.IsNullOrWhiteSpace(a.ExceptionMessage) ? DBNull.Value : Trunc(a.ExceptionMessage, 2000),
                    _ => throw new ArgumentOutOfRangeException(nameof(i), i, "Ordinal out of range.")
                };
            }

            public int FieldCount => Cols.Length;
            public string GetName(int i) => Cols[i];
            public int GetOrdinal(string name) => Array.IndexOf(Cols, name);
            public Type GetFieldType(int i) => typeof(object);
            public string GetDataTypeName(int i) => "variant";
            public bool IsDBNull(int i) => GetValue(i) == DBNull.Value;
            public object this[int i] => GetValue(i);
            public object this[string name] => GetValue(GetOrdinal(name));
            public int Depth => 0;
            public bool IsClosed => false;
            public int RecordsAffected => -1;

            public void Dispose() { }
            public bool NextResult() => false;
            public void Close() { }
            public DataTable GetSchemaTable() => null!;
            public int GetValues(object[] values) => throw new NotImplementedException();
            public bool GetBoolean(int i) => (bool)GetValue(i);
            public byte GetByte(int i) => (byte)GetValue(i);
            public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
            public char GetChar(int i) => (char)GetValue(i);
            public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
            public Guid GetGuid(int i) => (Guid)GetValue(i);
            public short GetInt16(int i) => Convert.ToInt16(GetValue(i));
            public int GetInt32(int i) => Convert.ToInt32(GetValue(i));
            public long GetInt64(int i) => Convert.ToInt64(GetValue(i));
            public float GetFloat(int i) => Convert.ToSingle(GetValue(i));
            public double GetDouble(int i) => Convert.ToDouble(GetValue(i));
            public string GetString(int i) => Convert.ToString(GetValue(i))!;
            public decimal GetDecimal(int i) => Convert.ToDecimal(GetValue(i));
            public DateTime GetDateTime(int i) => Convert.ToDateTime(GetValue(i));
            public IDataReader GetData(int i) => throw new NotImplementedException();
        }
    }
}
