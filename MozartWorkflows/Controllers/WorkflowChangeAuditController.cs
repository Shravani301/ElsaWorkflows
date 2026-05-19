using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MozartWorkflows.Models;
using MozartWorkflows.Services.Interfaces;
using System.Security.Claims;

namespace MozartWorkflows.Controllers;

/// <summary>
/// Exposes the workflow change-audit log to the Elsa Studio UI.
/// All endpoints require the user to be authenticated (cookie session).
/// </summary>
[ApiController]
[Route("api/workflow-change-audit")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class WorkflowChangeAuditController : ControllerBase
{
    private readonly IWorkflowChangeAuditService _auditService;

    public WorkflowChangeAuditController(IWorkflowChangeAuditService auditService)
        => _auditService = auditService;

    private bool CurrentIsAdmin =>
        User.FindFirst("IsAdmin")?.Value == "true";

    /// <summary>
    /// Returns the <paramref name="top"/> most-recent audit records across all workflows.
    /// GET /api/workflow-change-audit?top=200
    /// </summary>
    [HttpGet]
    public async Task<IEnumerable<WorkflowChangeAudit>> GetRecent([FromQuery] int top = 200)
        => await _auditService.GetRecentAsync(top);

    /// <summary>
    /// Returns a paged result with optional filters.
    /// GET /api/workflow-change-audit/paged?page=1&amp;pageSize=50&amp;workflowFilter=&amp;changeTypeFilter=
    /// </summary>
    [HttpGet("paged")]
    public async Task<PagedResult<WorkflowChangeAudit>> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? workflowFilter = null,
        [FromQuery] string? changeTypeFilter = null)
        => await _auditService.GetPagedAsync(page, pageSize, workflowFilter, changeTypeFilter);

    /// <summary>
    /// Returns all audit records for a specific workflow definition.
    /// GET /api/workflow-change-audit/{definitionId}
    /// </summary>
    [HttpGet("{definitionId}")]
    public async Task<IEnumerable<WorkflowChangeAudit>> GetByDefinition(string definitionId)
        => await _auditService.GetByDefinitionAsync(definitionId);

    /// <summary>
    /// Deletes selected audit records by ID. Admin only.
    /// DELETE /api/workflow-change-audit/selected
    /// </summary>
    [HttpDelete("selected")]
    public async Task<IActionResult> DeleteSelected([FromBody] DeleteSelectedRequest req)
    {
        if (!CurrentIsAdmin)
            return StatusCode(403, new { message = "Admin access required." });

        if (req?.Ids == null || !req.Ids.Any())
            return BadRequest(new { message = "No IDs provided." });

        await _auditService.DeleteAsync(req.Ids);
        return Ok(new { message = "Selected records deleted." });
    }
}

public record DeleteSelectedRequest(IEnumerable<long> Ids);
