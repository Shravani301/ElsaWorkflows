using MozartWorkflows.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace MozartWorkflows.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TestController : ControllerBase
    {
        public readonly ConfigManager _configManager;
        public TestController(ConfigManager configManager)
        {
            _configManager = configManager;
        }
        [HttpPost("GetConfigData")]
        public IActionResult UserLogin()
        {
            _configManager.GetConfigurationItem("DocumentFolder");
            return Ok();
        }


    }
}
