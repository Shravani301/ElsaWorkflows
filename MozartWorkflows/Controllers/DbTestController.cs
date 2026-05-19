using Dapper;
using MozartWorkflows.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MozartWorkflows.Controllers
{
    [ApiController]
    [ApiVersion("1.0")] // ✅ Declare API version
    [Route("api/v{version:apiVersion}/test")] // ✅ Include version in route
#pragma warning disable S6960
    public class DbTestController : ControllerBase
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IUserService _userService;

        public DbTestController(IDbConnectionFactory connectionFactory, IUserService userService)
        {
            _connectionFactory = connectionFactory;
            _userService = userService;
        }

        [HttpGet("ping-db")]
        public async Task<IActionResult> PingDatabase()
        {
            try
            {
                using var conn = _connectionFactory.CreateConnection();
                var result = await conn.QueryFirstOrDefaultAsync<string>("SELECT 'Connection successful'");
                return Ok(new { status = result });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetUsers()
        {
            try
            {
                var users = await _userService.GetUsersAsync();
                return Ok(users);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
#pragma warning restore S6960
}
