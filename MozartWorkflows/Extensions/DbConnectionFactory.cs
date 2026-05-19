using Microsoft.Data.SqlClient;
using MozartWorkflows.Services.Interfaces;
using MySqlConnector;
using Oracle.ManagedDataAccess.Client;
using System.Data;

namespace MozartWorkflows.Extensions
{
    public class DbConnectionFactory : IDbConnectionFactory
    {
        private const string SqlServerProvider = "SqlServer";
        private const string PostgreSqlProvider = "PostgreSql";
        private const string MySqlProvider = "MySql";
        private const string OracleProvider = "Oracle";

        private readonly IConfiguration _configuration;

        public DbConnectionFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IDbConnection CreateConnection()
        {
            return CreateConnection(null, null);
        }

        public IDbConnection CreateConnection(string? providerOverride, string? connectionStringOverride)
        {
            var provider = providerOverride ?? _configuration["DatabaseProvider"] ?? SqlServerProvider;
            var connectionString = connectionStringOverride ?? _configuration.GetConnectionString("connectionString");

            return provider switch
            {
                SqlServerProvider => new SqlConnection(connectionString),
                PostgreSqlProvider => new Npgsql.NpgsqlConnection(connectionString),
                MySqlProvider => new MySqlConnection(connectionString),
                OracleProvider => new OracleConnection(connectionString),
                _ => throw new NotSupportedException($"Provider '{provider}' is not supported.")
            };
        }
    }
}
