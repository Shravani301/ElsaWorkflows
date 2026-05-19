namespace MozartWorkflows.Models;

/// <summary>
/// Records who changed a workflow definition, when, and what kind of change it was.
/// Persisted to the WorkflowChangeAudit table.
/// </summary>
public class WorkflowChangeAudit
{
    public long    Id              { get; set; }
    public string  DefinitionId    { get; set; } = string.Empty;
    public string  WorkflowName    { get; set; } = string.Empty;
    public int     Version         { get; set; }

    /// <summary>Created | Duplicated | Saved | Published | Unpublished | Deleted</summary>
    public string  ChangeType      { get; set; } = string.Empty;

    public string  ChangedBy       { get; set; } = string.Empty;

    /// <summary>UserId from ElsaDashboardUsers (string form of the int PK).</summary>
    public string? ChangedByUserId { get; set; }

    public DateTime ChangedAt      { get; set; }
    public int?    ActivityCount   { get; set; }

    /// <summary>
    /// JSON: { displayName, description, isPublished, activityTypes[],
    ///         workflowDataBefore (full Elsa Data JSON of previous version),
    ///         workflowDataAfter  (full Elsa Data JSON of current version) }
    /// </summary>
    public string? ChangeDetails   { get; set; }

    public string? IpAddress       { get; set; }
}
