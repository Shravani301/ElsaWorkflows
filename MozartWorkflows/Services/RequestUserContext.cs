namespace MozartWorkflows.Services;

/// <summary>
/// Scoped service (one per HTTP request) that holds the resolved user identity.
/// Middleware populates this early in the pipeline so that downstream
/// notification handlers (Elsa events) can record who made the change
/// even when Elsa's own API paths skip Cookie authentication.
/// </summary>
public class RequestUserContext
{
    public string  Username  { get; set; } = "unknown";
    public string? UserId    { get; set; }
    public string  IpAddress { get; set; } = string.Empty;
}
