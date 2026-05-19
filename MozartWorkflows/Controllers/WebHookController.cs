using Microsoft.AspNetCore.Mvc;
using MozartWorkflows.Services.Interfaces;

namespace MozartWorkflows.Controllers
{
    public sealed class WebhookNotificationRequest
    {
        public List<WebhookNotificationItem>? Value { get; set; }
    }

    public sealed class WebhookNotificationItem
    {
        public string? Resource { get; set; }
        public string? ChangeType { get; set; }
    }

    [ApiController]
    [Route("api/webhook")]
    public class WebHookController:ControllerBase
    {
        private readonly ITeamsMeetingService _service;

        public WebHookController(ITeamsMeetingService service)
        {
            _service = service;
        }

        [HttpPost]
        public async Task<IActionResult> ReceiveNotification([FromBody] WebhookNotificationRequest? body, [FromQuery] string? validationToken)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(validationToken))
                {
                    Console.WriteLine($"[VALIDATION TOKEN RECEIVED]: {validationToken}");
                    return Content(validationToken, "text/plain");
                }

                Console.WriteLine("[WEBHOOK CALLED FOR EVENT]");

                if (body?.Value == null)
                    return Ok();

                foreach (var item in body.Value)
                {
                    string? resource = item.Resource;
                    string? changeType = item.ChangeType;

                    if (string.IsNullOrEmpty(resource) || string.IsNullOrEmpty(changeType))
                        continue;

                    var parts = resource.Split('/');
                    if (parts.Length < 4) continue;

                    var userId = parts[1];
                    var eventId = parts[3];

                    Console.WriteLine($"[EVENT] {changeType} - {eventId}");

                    await _service.ProcessWebhookEvent(userId, eventId, changeType);
                }

                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WEBHOOK ERROR] {ex.Message}");
                return StatusCode(500);
            }
        }
    }
}

