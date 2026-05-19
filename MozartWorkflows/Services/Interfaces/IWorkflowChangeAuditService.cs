using MozartWorkflows.Models;

namespace MozartWorkflows.Services.Interfaces;

public interface IWorkflowChangeAuditService
{
    /// <summary>Creates/migrates the WorkflowChangeAudit table.</summary>
    Task EnsureTableExistsAsync();

    /// <summary>Appends one audit record.</summary>
    Task LogAsync(WorkflowChangeAudit audit);

    /// <summary>
    /// Removes any 'Saved' record written in the last 30 seconds for the same
    /// DefinitionId+Version. Called by Published/Unpublished handlers to
    /// eliminate the redundant Saved row that Elsa auto-emits during publish.
    /// </summary>
    Task RemoveRecentSavedAsync(string definitionId, int version);

    /// <summary>Returns the most recent <paramref name="top"/> records across all workflows.</summary>
    Task<IEnumerable<WorkflowChangeAudit>> GetRecentAsync(int top = 200);

    /// <summary>Returns all audit records for a specific workflow definition.</summary>
    Task<IEnumerable<WorkflowChangeAudit>> GetByDefinitionAsync(string definitionId);

    /// <summary>Returns a paged result with optional filters.</summary>
    Task<PagedResult<WorkflowChangeAudit>> GetPagedAsync(int page, int pageSize,
        string? workflowFilter = null, string? changeTypeFilter = null);

    /// <summary>Deletes audit records by their IDs.</summary>
    Task DeleteAsync(IEnumerable<long> ids);
}
