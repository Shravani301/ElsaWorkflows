using Dapper;
using Elsa.Events;
using MediatR;
using MozartWorkflows.Models;
using MozartWorkflows.Services;
using MozartWorkflows.Services.Interfaces;
using System.Text.Json;

namespace MozartWorkflows.Elsa.NotificationHandlers;

/// <summary>
/// Listens to Elsa v2 workflow-definition lifecycle events and writes an audit
/// record to the WorkflowChangeAudit table for every meaningful change.
///
/// Deduplication:
///   Elsa emits WorkflowDefinitionSaved as a side-effect of Publish and Retract.
///   When Published/Retracted fires we call RemoveRecentSavedAsync to delete the
///   redundant Saved row that was already written for the same definition+version
///   in the last 30 seconds.
///
/// WorkflowDefinitions.Data:
///   The handler queries Elsa.WorkflowDefinitions directly (via IDbConnectionFactory)
///   to capture the full workflow JSON both BEFORE (previous version) and AFTER
///   (current version) the change and stores them inside ChangeDetails.
/// </summary>
public class WorkflowDefinitionChangeHandler :
    INotificationHandler<WorkflowDefinitionSaved>,
    INotificationHandler<WorkflowDefinitionPublished>,
    INotificationHandler<WorkflowDefinitionRetracted>,
    INotificationHandler<WorkflowDefinitionDeleted>
{
    private const string UnnamedWorkflow = "(unnamed)";
    private const string PublishedChangeType = "Published";
    private const string UnpublishedChangeType = "Unpublished";
    private const string DeletedChangeType = "Deleted";

    private readonly IWorkflowChangeAuditService _auditService;
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly RequestUserContext _userContext;
    private readonly ILogger<WorkflowDefinitionChangeHandler> _logger;
    private readonly string _provider;

    public WorkflowDefinitionChangeHandler(
        IWorkflowChangeAuditService auditService,
        IDbConnectionFactory dbConnectionFactory,
        RequestUserContext userContext,
        IConfiguration config,
        ILogger<WorkflowDefinitionChangeHandler> logger)
    {
        _auditService = auditService;
        _dbConnectionFactory = dbConnectionFactory;
        _userContext = userContext;
        _logger = logger;
        _provider = config["DatabaseProvider"] ?? "SqlServer";
    }

    public async Task Handle(WorkflowDefinitionSaved notification, CancellationToken cancellationToken)
    {
        var definition = notification.WorkflowDefinition;
        var source = CreateAuditSource(
            definition.DefinitionId,
            definition.DisplayName ?? definition.Name ?? UnnamedWorkflow,
            definition.Version,
            definition.Activities?.Select(activity => activity.Type).Distinct().ToList(),
            new { definition.DisplayName, definition.Description, definition.IsPublished });

        var changeType = ResolveChangeType(definition.Version, definition.Name ?? string.Empty, definition.DisplayName ?? string.Empty);
        var workflowData = await GetWorkflowDataAsync(definition.DefinitionId, definition.Version);

        await TryLogAsync(BuildAudit(source, changeType, workflowData));
    }

    public async Task Handle(WorkflowDefinitionPublished notification, CancellationToken cancellationToken)
    {
        var definition = notification.WorkflowDefinition;
        await TryRemoveSavedAsync(definition.DefinitionId, definition.Version);

        var source = CreateAuditSource(
            definition.DefinitionId,
            definition.DisplayName ?? definition.Name ?? UnnamedWorkflow,
            definition.Version,
            definition.Activities?.Select(activity => activity.Type).Distinct().ToList(),
            new { definition.DisplayName, definition.Description, definition.IsPublished });

        var workflowData = await GetWorkflowDataAsync(definition.DefinitionId, definition.Version);
        await TryLogAsync(BuildAudit(source, PublishedChangeType, workflowData));
    }

    public async Task Handle(WorkflowDefinitionRetracted notification, CancellationToken cancellationToken)
    {
        var definition = notification.WorkflowDefinition;
        await TryRemoveSavedAsync(definition.DefinitionId, definition.Version);

        var source = CreateAuditSource(
            definition.DefinitionId,
            definition.DisplayName ?? definition.Name ?? UnnamedWorkflow,
            definition.Version,
            definition.Activities?.Select(activity => activity.Type).Distinct().ToList(),
            new { definition.DisplayName, definition.Description, definition.IsPublished });

        var workflowData = await GetWorkflowDataAsync(definition.DefinitionId, definition.Version);
        await TryLogAsync(BuildAudit(source, UnpublishedChangeType, workflowData));
    }

    public async Task Handle(WorkflowDefinitionDeleted notification, CancellationToken cancellationToken)
    {
        var definition = notification.WorkflowDefinition;
        var source = CreateAuditSource(
            definition.DefinitionId,
            definition.DisplayName ?? definition.Name ?? UnnamedWorkflow,
            definition.Version,
            null,
            new { definition.DisplayName, definition.Description, definition.IsPublished });

        await TryLogAsync(BuildAudit(source, DeletedChangeType, (null, null)));
    }

    private async Task<(string? before, string? after)> GetWorkflowDataAsync(string definitionId, int version)
    {
        try
        {
            using var conn = _dbConnectionFactory.CreateConnection();
            var sqlAfter = GetWorkflowDataQuery(isPreviousVersion: false);
            var after = await conn.QueryFirstOrDefaultAsync<string>(
                sqlAfter,
                new { DefinitionId = definitionId, Version = version });

            string? before = null;
            if (version > 1)
            {
                var sqlBefore = GetWorkflowDataQuery(isPreviousVersion: true);
                before = await conn.QueryFirstOrDefaultAsync<string>(
                    sqlBefore,
                    new { DefinitionId = definitionId, PrevVersion = version - 1 });
            }

            return (before, after);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not read WorkflowDefinitions.Data for DefinitionId={DefinitionId} Version={Version}",
                definitionId,
                version);
            return (null, null);
        }
    }

    private string GetWorkflowDataQuery(bool isPreviousVersion)
    {
        if (_provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            return isPreviousVersion
                ? @"SELECT ""Data"" FROM elsa.""WorkflowDefinitions""
                          WHERE ""DefinitionId"" = @DefinitionId AND ""Version"" = @PrevVersion
                          LIMIT 1"
                : @"SELECT ""Data"" FROM elsa.""WorkflowDefinitions""
                      WHERE ""DefinitionId"" = @DefinitionId AND ""Version"" = @Version
                      LIMIT 1";
        }

        return isPreviousVersion
            ? @"SELECT TOP 1 Data FROM Elsa.WorkflowDefinitions
                          WHERE DefinitionId = @DefinitionId AND Version = @PrevVersion"
            : @"SELECT TOP 1 Data FROM Elsa.WorkflowDefinitions
                      WHERE DefinitionId = @DefinitionId AND Version = @Version";
    }

    private static string ResolveChangeType(int version, string name, string displayName)
    {
        if (version == 1)
        {
            var isCopy = name.StartsWith("Copy of ", StringComparison.OrdinalIgnoreCase)
                || displayName.StartsWith("Copy of ", StringComparison.OrdinalIgnoreCase);
            return isCopy ? "Duplicated" : "Created";
        }

        return "Saved";
    }

    private WorkflowChangeAudit BuildAudit(
        WorkflowAuditSource source,
        string changeType,
        (string? before, string? after) workflowData)
    {
        var (parsedBefore, parsedAfter) = ParseWorkflowData(workflowData);

        return new WorkflowChangeAudit
        {
            DefinitionId = source.DefinitionId,
            WorkflowName = source.WorkflowName,
            Version = source.Version,
            ChangeType = changeType,
            ChangedBy = _userContext.Username,
            ChangedByUserId = _userContext.UserId,
            ActivityCount = source.ActivityTypes?.Count,
            ChangeDetails = JsonSerializer.Serialize(new
            {
                snapshot = source.Snapshot,
                activityTypes = source.ActivityTypes ?? new List<string>(),
                workflowDataBefore = parsedBefore,
                workflowDataAfter = parsedAfter
            }),
            IpAddress = _userContext.IpAddress
        };
    }

    private static WorkflowAuditSource CreateAuditSource(
        string definitionId,
        string workflowName,
        int version,
        List<string>? activityTypes,
        object snapshot) =>
        new(definitionId, workflowName, version, activityTypes, snapshot);

    private static (object? before, object? after) ParseWorkflowData((string? before, string? after) workflowData) =>
        (TryParseWorkflowData(workflowData.before), TryParseWorkflowData(workflowData.after));

    private static object? TryParseWorkflowData(string? workflowData)
    {
        if (workflowData is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<object>(workflowData);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task TryLogAsync(WorkflowChangeAudit audit)
    {
        try
        {
            await _auditService.LogAsync(audit);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "WorkflowDefinitionChangeHandler: failed to write audit record DefinitionId={DefinitionId} ChangeType={ChangeType}",
                audit.DefinitionId,
                audit.ChangeType);
        }
    }

    private async Task TryRemoveSavedAsync(string definitionId, int version)
    {
        try
        {
            await _auditService.RemoveRecentSavedAsync(definitionId, version);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "WorkflowDefinitionChangeHandler: failed to remove duplicate Saved record DefinitionId={DefinitionId} Version={Version}",
                definitionId,
                version);
        }
    }

    private sealed record WorkflowAuditSource(
        string DefinitionId,
        string WorkflowName,
        int Version,
        List<string>? ActivityTypes,
        object Snapshot);
}
