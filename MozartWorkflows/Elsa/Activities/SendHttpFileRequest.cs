using Elsa;
using Elsa.Activities.Http.Contracts;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Http.Options;
using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Design;
using Elsa.Expressions;
using Elsa.Metadata;
using Elsa.Services;
using Elsa.Services.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using HttpRequestHeaders = Elsa.Activities.Http.Models.HttpRequestHeaders;

namespace MozartWorkflows.Elsa.Activities
{

[Action(
    Category = "HTTP",
    DisplayName = "Send HTTP File",
    Description = "Reads multipart/form-data file only and forwards it as multipart/form-data or raw binary. Outputs same fields as Elsa SendHttpRequest.",
    Outcomes = new[] { OutcomeNames.Done, "Unsupported Status Code" }
)]
public sealed class SendHttpFileRequest : Activity, IActivityPropertyOptionsProvider
{
    private const string BinaryMode = "Binary";
    private const string MultipartMode = "Multipart";
    private const string DefaultFileContentType = "application/octet-stream";
    private static readonly int[] DefaultSupportedStatusCodes = { 200 };

    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SendHttpFileRequest> _logger;
    private readonly IEnumerable<IHttpResponseContentReader> _parsers;
    private readonly string? _defaultContentParserName;

    public SendHttpFileRequest(
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        IEnumerable<IHttpResponseContentReader> parsers,
        IOptions<HttpActivityOptions> options,
        ILogger<SendHttpFileRequest> logger)
    {
        _httpClient = httpClientFactory.CreateClient(nameof(SendHttpFileRequest));
        _httpContextAccessor = httpContextAccessor;
        _parsers = parsers;
        _defaultContentParserName = options.Value.DefaultContentParserName;
        _logger = logger;
    }

    [ActivityInput(Hint = "The URL to send the HTTP request to.", SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid })]
    public Uri? Url { get; set; }

    [ActivityInput(
        UIHint = ActivityInputUIHints.Dropdown,
        Hint = "The HTTP method to use when making the request.",
        Options = new[] { "POST", "PUT", "PATCH" },
        DefaultValue = "POST",
        SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
    )]
    public string? Method { get; set; } = "POST";

    [ActivityInput(
        Label = "Send Mode",
        Hint = "Multipart = forward as multipart/form-data, Binary = forward as raw request body (Postman -> body -> binary). Auto defaults to Multipart.",
        UIHint = ActivityInputUIHints.Dropdown,
        Options = new[] { "Auto", "Multipart", "Binary" },
        DefaultValue = "Auto",
        SupportedSyntaxes = new[] { SyntaxNames.Literal }
    )]
    public string SendMode { get; set; } = "Auto";

    [ActivityInput(
        Label = "Incoming File Field Name",
        Hint = "Form-data key name coming from client (usually 'file').",
        DefaultValue = "file",
        SupportedSyntaxes = new[] { SyntaxNames.Literal, SyntaxNames.JavaScript, SyntaxNames.Liquid }
    )]
    public string IncomingFileFieldName { get; set; } = "file";

    [ActivityInput(
        Label = "Outgoing File Field Name",
        Hint = "Form-data key name expected by the 3rd party API (usually 'file').",
        DefaultValue = "file",
        SupportedSyntaxes = new[] { SyntaxNames.Literal, SyntaxNames.JavaScript, SyntaxNames.Liquid }
    )]
    public string OutgoingFileFieldName { get; set; } = "file";

    [ActivityInput(
        Label = "Authorization",
        Hint = "The Authorization header value to send.",
        Category = PropertyCategories.Advanced,
        SupportedSyntaxes = new[] { SyntaxNames.Literal, SyntaxNames.JavaScript, SyntaxNames.Liquid }
    )]
    public string? Authorization { get; set; }

    [ActivityInput(
        Hint = "Additional headers to send along with the request.",
        UIHint = ActivityInputUIHints.MultiLine,
        DefaultSyntax = SyntaxNames.Json,
        SupportedSyntaxes = new[] { SyntaxNames.Json, SyntaxNames.JavaScript, SyntaxNames.Liquid },
        Category = PropertyCategories.Advanced
    )]
    public HttpRequestHeaders RequestHeaders { get; set; } = new();

    [ActivityInput(Hint = "Read the content of the response.", SupportedSyntaxes = new[] { SyntaxNames.Literal, SyntaxNames.JavaScript, SyntaxNames.Liquid })]
    public bool ReadContent { get; set; } = true;

    [ActivityInput(
        Label = "Response Content Parser",
        Hint = "The parser to use to parse the response content.",
        SupportedSyntaxes = new[] { SyntaxNames.Literal, SyntaxNames.JavaScript, SyntaxNames.Liquid },
        UIHint = ActivityInputUIHints.Dropdown,
        OptionsProvider = typeof(SendHttpFileRequest)
    )]
    public string? ResponseContentParserName { get; set; }

    [ActivityInput(
        Hint = "A list of possible HTTP status codes to handle.",
        UIHint = ActivityInputUIHints.MultiText,
        DefaultSyntax = SyntaxNames.Json,
        DefaultValue = typeof(SendHttpFileRequest),
        SupportedSyntaxes = new[] { SyntaxNames.Json, SyntaxNames.JavaScript, SyntaxNames.Liquid },
        ConsiderValuesAsOutcomes = true,
        IsDesignerCritical = true
    )]
    public ICollection<int>? SupportedStatusCodes { get; set; } = new HashSet<int>(DefaultSupportedStatusCodes);

    [ActivityInput(
        Label = "Timeout Seconds",
        Hint = "Optional timeout (seconds). 0 = default HttpClient timeout.",
        DefaultValue = 0,
        SupportedSyntaxes = new[] { SyntaxNames.Literal, SyntaxNames.JavaScript, SyntaxNames.Liquid },
        Category = PropertyCategories.Advanced
    )]
    public int TimeoutSeconds { get; set; }

    [ActivityOutput] public HttpResponseModel? Response { get; set; }
    [ActivityOutput] public object? ResponseContent { get; set; }

    protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            var requestContext = await CreateRequestContextAsync(context);
            var mode = ResolveMode(SendMode);
            using var request = CreateRequest(requestContext, mode);

            LogRequest(mode, requestContext);

            using var response = await _httpClient.SendAsync(request, context.CancellationToken);

            Response = new HttpResponseModel
            {
                StatusCode = response.StatusCode,
                Headers = BuildHeaders(response)
            };

            if (response.Content != null && ReadContent)
            {
                if (response.IsSuccessStatusCode)
                {
                    var contentType = response.Content.Headers.ContentType?.MediaType;
                    var formatter = SelectContentParser(ResponseContentParserName, contentType);
                    ResponseContent = await formatter.ReadAsync(response, this, context.CancellationToken);
                }
                else
                {
                    ResponseContent = await response.Content.ReadAsStringAsync();
                }
            }

            var statusCode = (int)response.StatusCode;
            var outcomes = new List<string> { OutcomeNames.Done, statusCode.ToString() };

            if (!IsSupportedStatusCode(statusCode))
                outcomes.Add("Unsupported Status Code");

            return Outcomes(outcomes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendHttpFileRequest failed.");
            context.SetVariable("SendHttpFileRequest_Error", ex.ToString());
            return Fault(ex.Message);
        }
    }

    private static string ResolveMode(string? sendMode) =>
        string.Equals(sendMode, BinaryMode, StringComparison.OrdinalIgnoreCase) ? BinaryMode : MultipartMode;

    private async Task<SendHttpFileRequestContext> CreateRequestContextAsync(ActivityExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(Url);

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available. This activity must run under an HTTP-triggered workflow.");

        if (!httpContext.Request.HasFormContentType)
            throw new InvalidOperationException("Incoming request must be multipart/form-data.");

        var incomingFile = httpContext.Request.Form.Files.GetFile(IncomingFileFieldName);
        if (incomingFile == null || incomingFile.Length == 0)
            throw new InvalidOperationException($"No file found under key '{IncomingFileFieldName}'.");

        var fileBytes = await ReadFileBytesAsync(incomingFile, context);
        var fileContentType = string.IsNullOrWhiteSpace(incomingFile.ContentType) ? DefaultFileContentType : incomingFile.ContentType;

        return new SendHttpFileRequestContext(Url, incomingFile, fileBytes, fileContentType);
    }

    private static async Task<byte[]> ReadFileBytesAsync(IFormFile incomingFile, ActivityExecutionContext context)
    {
        await using var input = incomingFile.OpenReadStream();
        using var ms = new MemoryStream();
        await input.CopyToAsync(ms, context.CancellationToken);
        return ms.ToArray();
    }

    private HttpRequestMessage CreateRequest(SendHttpFileRequestContext requestContext, string mode)
    {
        var method = Method ?? HttpMethods.Post;
        var request = new HttpRequestMessage(new HttpMethod(method), requestContext.Url);

        ApplyTimeout();
        ApplyAuthorization(request);
        ApplyRequestHeaders(request);
        request.Content = CreateRequestContent(requestContext, mode);

        return request;
    }

    private void ApplyTimeout()
    {
        if (TimeoutSeconds > 0)
            _httpClient.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);
    }

    private void ApplyAuthorization(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(Authorization))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(Authorization);
    }

    private void ApplyRequestHeaders(HttpRequestMessage request)
    {
        var requestHeaders = new HeaderDictionary(RequestHeaders.ToDictionary(x => x.Key, x => new StringValues(x.Value.Split(','))));

        foreach (var header in requestHeaders)
            request.Headers.TryAddWithoutValidation(header.Key, header.Value.AsEnumerable());
    }

    private HttpContent CreateRequestContent(SendHttpFileRequestContext requestContext, string mode)
    {
        if (string.Equals(mode, BinaryMode, StringComparison.Ordinal))
            return CreateBinaryContent(requestContext.FileBytes, requestContext.FileContentType);

        return CreateMultipartContent(requestContext);
    }

    private static HttpContent CreateBinaryContent(byte[] fileBytes, string fileContentType)
    {
        var content = new ByteArrayContent(fileBytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(fileContentType);
        return content;
    }

    private HttpContent CreateMultipartContent(SendHttpFileRequestContext requestContext)
    {
        var multipart = new MultipartFormDataContent();
        var filePart = new ByteArrayContent(requestContext.FileBytes);
        filePart.Headers.ContentType = MediaTypeHeaderValue.Parse(requestContext.FileContentType);
        multipart.Add(filePart, OutgoingFileFieldName, requestContext.IncomingFile.FileName);
        return multipart;
    }

    private void LogRequest(string mode, SendHttpFileRequestContext requestContext)
    {
        _logger.LogInformation(
            "SendHttpFileRequest sending. Mode={Mode}, Url={Url}, File={File}, Size={Size}, ContentType={CT}",
            mode, requestContext.Url, requestContext.IncomingFile.FileName, requestContext.FileBytes.Length, requestContext.FileContentType);
    }

    private static Dictionary<string, string[]> BuildHeaders(HttpResponseMessage response)
    {
        var responseHeaders = response.Headers.ToDictionary(x => x.Key, x => x.Value.ToArray());
        var contentHeaders = response.Content?.Headers.ToDictionary(x => x.Key, x => x.Value.ToArray())
            ?? Enumerable.Empty<KeyValuePair<string, string[]>>();

        return new Dictionary<string, string[]>(responseHeaders.Concat(contentHeaders));
    }

    private bool IsSupportedStatusCode(int statusCode)
    {
        var supportedStatusCodes = SupportedStatusCodes;
        return supportedStatusCodes == null || supportedStatusCodes.Count == 0 || supportedStatusCodes.Contains(statusCode);
    }

    private IHttpResponseContentReader SelectContentParser(string? parserName, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(parserName))
        {
            if (_defaultContentParserName != null)
            {
                var defaultParser = _parsers.FirstOrDefault(x => x.Name == _defaultContentParserName);
                if (defaultParser != null)
                    return defaultParser;
            }

            var simpleContentType = ExtractMediaType(contentType);
            var parsers = _parsers.OrderByDescending(x => x.Priority).ToList();
            return parsers.FirstOrDefault(x => x.GetSupportsContentType(simpleContentType)) ?? parsers[^1];
        }

        var parser = _parsers.FirstOrDefault(x => x.Name == parserName);
        if (parser == null)
            throw new InvalidOperationException("The specified parser does not exist");

        return parser;
    }

    object? IActivityPropertyOptionsProvider.GetOptions(System.Reflection.PropertyInfo property)
    {
        if (property.Name != nameof(ResponseContentParserName))
            return null;

        var items = _parsers.Select(x => new SelectListItem(x.Name, x.Name)).ToList();
        items.Insert(0, new SelectListItem("Auto Select", ""));
        return items;
    }

    private static string ExtractMediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return string.Empty;

        var separatorIndex = contentType.IndexOf(';');
        return separatorIndex >= 0 ? contentType[..separatorIndex] : contentType;
    }

    private sealed record SendHttpFileRequestContext(Uri Url, IFormFile IncomingFile, byte[] FileBytes, string FileContentType);
}

} // namespace MozartWorkflows.Elsa.Activities
