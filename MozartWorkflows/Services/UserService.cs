using Dapper;
using MozartWorkflows;
using MozartWorkflows.Dtos;
using MozartWorkflows.Services.Interfaces;

namespace MozartWorkflows.Services
{
    public class UserService:IUserService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public UserService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<UserDto>> GetUsersAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            string query = SqlQueries.SqlServer.GetUsers;
            return await conn.QueryAsync<UserDto>(query);
        }
    }
}
