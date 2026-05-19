using System;
using Microsoft.AspNetCore.Mvc;
using MozartWorkflows.Dtos;
using MozartWorkflows.Services.Interfaces;

namespace MozartWorkflows.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MeetingController : ControllerBase
    {
        private readonly ITeamsMeetingService _service;
        
        public MeetingController(ITeamsMeetingService service)
        {
            _service = service;
        }

        [HttpPost("CreateMeeting")]
        public async Task<IActionResult> CreateMeeting([FromBody] CreateMeetingRequest request)
        {
            var result = await _service.CreateMeetingAsync(request);
            return Ok(result);
        }

        [HttpPut("UpdateMeeting/{meetingId}")]
        public async Task<IActionResult> UpdateMeeting(string meetingId, [FromBody] CreateMeetingRequest request)
        {
            var result = await _service.UpdateMeetingAsync(meetingId, request);
            return Ok(result);
        }
        [HttpDelete("{meetingId}")]
       public async Task<IActionResult> CancelMeeting(string meetingId, [FromQuery] string userId)
       {
            var result = await _service.CancelMeetingAsync(meetingId, userId);

              if (result)
               return Ok(new { message = "Meeting cancelled successfully" });

          return BadRequest("Failed to cancel meeting");
       }
    }
}
