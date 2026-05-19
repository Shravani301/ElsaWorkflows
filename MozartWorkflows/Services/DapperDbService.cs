using Dapper;
using MozartWorkflows.Services.Interfaces;
using System.Data;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;

namespace MozartWorkflows.Services
{
    public class DapperDbService : IDbService
    {
        private static readonly Regex SafeIdentifierRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        private readonly IDbConnectionFactory _connectionFactory;

        public DapperDbService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        private IDbConnection GetConnection() => _connectionFactory.CreateConnection();

        public int Add<T>(T entity, string sqlQuery)
        {
            return ExecuteEntityCommand(entity, sqlQuery);
        }

        public int Update<T>(T entity, string sqlQuery)
        {
            return ExecuteEntityCommand(entity, sqlQuery);
        }

        public int Delete(int id, string sqlQuery)
        {
            using var connection = GetConnection();
            connection.Open();
            return connection.Execute(sqlQuery, new { Id = id });
        }

        public List<T> GetAll<T>(string sqlQuery)
        {
            using var connection = GetConnection();
            connection.Open();
            return connection.Query<T>(sqlQuery).ToList();
        }

        public List<T> GetAllWithParams<T>(string query, IEnumerable<int> Id)
        {
            using var connection = GetConnection();
            connection.Open();
            return connection.Query<T>(query, new { Id }).ToList();
        }

        public List<T> ExecuteProcedure<T>(string procName, object param)
        {
            using var connection = GetConnection();
            connection.Open();
            var result = connection.Query<T>(procName, param, commandType: CommandType.StoredProcedure, commandTimeout: 0).ToList();
            return result;
        }

        public async Task<IEnumerable<T>> ExecuteProcedureAsync<T>(string procName, object param)
        {
            using var connection = GetConnection();
            connection.Open();
            var result = await connection.QueryAsync<T>(procName, param, commandType: CommandType.StoredProcedure, commandTimeout: 0);
            return result;
        }

        public async Task ExecuteProcedureAsync(string procName, object param)
        {
            using var connection = GetConnection();
            connection.Open();
            await connection.ExecuteAsync(procName, param, commandType: CommandType.StoredProcedure, commandTimeout: 0);
        }

        public void ExecuteProcedureVoid(string procName, object param)
        {
            using var connection = GetConnection();
            connection.Open();
            connection.Query(procName, param, commandType: CommandType.StoredProcedure, commandTimeout: 0);
        }

        public T ExecuteProcSingle<T>(string procName, object param)
        {
            using var connection = GetConnection();
            connection.Open();
            var result = connection.QuerySingle<T>(procName, param, commandType: CommandType.StoredProcedure, commandTimeout: 0);
            return result;
        }

        public int ExecuteProc(string procName, object param)
        {
            using var connection = GetConnection();
            connection.Open();
            int result = connection.Execute(procName, param, commandType: CommandType.StoredProcedure, commandTimeout: 0);
            return result;
        }

        public T? GetById<T>(int id, string sqlQuery)
        {
            using var connection = GetConnection();
            connection.Open();
            return connection.QuerySingleOrDefault<T>(sqlQuery, new { Id = id });
        }

        public T QuerySingle<T>(string sqlQuery)
        {
            using var connection = GetConnection();
            connection.Open();
            return connection.QuerySingle<T>(sqlQuery);
        }

        public List<int> Query(string query)
        {
            using var connection = GetConnection();
            connection.Open();
            return connection.Query<int>(query).ToList();
        }

        public List<T> QueryList<T>(string query)
        {
            using var connection = GetConnection();
            connection.Open();
            return connection.Query<T>(query).ToList();
        }

        public async Task<IEnumerable<T>> GetAllAsync<T>(string query, object? parameters = null)
        {
            using var connection = GetConnection();
            return await connection.QueryAsync<T>(query, parameters);
        }

        public async Task<T?> GetAsync<T>(string query, object? parameters = null)
        {
            using var connection = GetConnection();
            return (await connection.QueryAsync<T>(query, parameters)).FirstOrDefault();
        }

        public async Task<int> EditData(string command, object parms)
        {
            using var connection = GetConnection();
            int result = await connection.ExecuteAsync(command, parms);
            return result;
        }

        public List<T> ExecuteProcZeroTimeout<T>(string procName, object param)
        {
            using var connection = GetConnection();
            connection.Open();
            var result = connection.Query<T>(procName, param, commandType: CommandType.StoredProcedure, commandTimeout: 0).ToList();
            return result;
        }

#pragma warning disable S2077
        public async Task InsertJsonDataAsync(string tableName, string jsonData)
        {
            if (!SafeIdentifierRegex.IsMatch(tableName))
                throw new ArgumentException("Table name contains unsupported characters.", nameof(tableName));

            var sql = $"INSERT INTO {tableName} (JsonColumn) VALUES (@JsonData)";
            using var connection = GetConnection();
            connection.Open();
            await connection.ExecuteAsync(sql, new { JsonData = jsonData });
        }
#pragma warning restore S2077

        public async Task<List<List<object>>> ExecuteMultipleTablesAsync(string procName, object param)
        {
            using var connection = GetConnection();
            connection.Open();
            using (var result = await connection.QueryMultipleAsync(procName, param, commandType: CommandType.StoredProcedure, commandTimeout: 0))
            {
                var tables = new List<List<object>>();
                while (!result.IsConsumed)
                {
                    var table = await result.ReadAsync<object>();
                    tables.Add(table.ToList());
                }
                return tables;
            }
        }

        public void Execute(string query, object? parameters = null)
        {
            using var connection = GetConnection();
            connection.Open();
            connection.Execute(query, parameters);
        }

        public T? QueryFirstOrDefault<T>(string query, object? parameters = null)
        {
            using var connection = GetConnection();
            connection.Open();
            return connection.QueryFirstOrDefault<T>(query, parameters);
        }

        public async Task ExecuteAsync(string query, object? parameters = null)
        {
            using var connection = GetConnection();
            await connection.ExecuteAsync(query, parameters);
        }

        public async Task<T?> QueryFirstOrDefaultAsync<T>(string query, object? parameters = null)
        {
            using var connection = GetConnection();
            return await connection.QueryFirstOrDefaultAsync<T>(query, parameters);
        }

        public async Task<IEnumerable<T>> QueryAsync<T>(string query, object? parameters = null)
        {
            using var connection = GetConnection();
            return await connection.QueryAsync<T>(query, parameters);
        }

        public void ExecuteProcedurevoid(string procName, object param) =>
            ExecuteProcedureVoid(procName, param);

        private int ExecuteEntityCommand<T>(T entity, string sqlQuery)
        {
            using var connection = GetConnection();
            connection.Open();
            return connection.Execute(sqlQuery, entity);
        }
    }
}
