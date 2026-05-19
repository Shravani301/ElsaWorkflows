using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;
using MozartWorkflows.PhoneCall.Models;

namespace MozartWorkflows.Elsa.Activities;

[Activity(Category = "Twilio", DisplayName = "Make Call (Agent → Customer with Fallback)")]
public class TwilioMakeCall : Activity
{
    private static readonly Regex NonDigitsRegex = new(@"\D", RegexOptions.Compiled, TimeSpan.FromSeconds(2));

    [ActivityInput(
        Label = "Customer Number",
        Hint = "The customer's phone (10 digits, 91-prefixed or +91)",
        SupportedSyntaxes = new[] { SyntaxNames.Literal, SyntaxNames.JavaScript })]
    public string CustomerNumber { get; set; } = default!;

    [ActivityInput(
        Label = "Alternate Customer Number",
        Hint = "Optional fallback number if customer doesn't pick up",
        SupportedSyntaxes = new[] { SyntaxNames.Literal, SyntaxNames.JavaScript })]
    public string? AlternateCustomerNumber { get; set; }

    [ActivityInput(
        Label = "Agent Number",
        Hint = "The agent to ring first",
        SupportedSyntaxes = new[] { SyntaxNames.Literal, SyntaxNames.JavaScript })]
    public string AgentNumber { get; set; } = default!;

    [ActivityOutput] public string CallSid { get; private set; } = default!;

    private readonly TwilioSettings _cfg;
    public TwilioMakeCall(IOptions<TwilioSettings> cfg) => _cfg = cfg.Value;

    protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
    {
        TwilioClient.Init(_cfg.AccountSid, _cfg.AuthToken);

        // Normalize numbers
        var customer = NormaliseIndianNumber(CustomerNumber);
        var agent = NormaliseIndianNumber(AgentNumber);
        var alternate = string.IsNullOrWhiteSpace(AlternateCustomerNumber) ? null : NormaliseIndianNumber(AlternateCustomerNumber);

        // TwiML generation
        var twimlBuilder = new System.Text.StringBuilder();
        twimlBuilder.AppendLine("<Response>");
        twimlBuilder.AppendLine(@"<Say voice=""Polly.Amy"">Please wait while I connect the customer.</Say>");

        // Primary customer
        twimlBuilder.AppendLine($@"<Dial callerId=""{_cfg.FromNumber}"" timeout=""20"" record=""record-from-answer-dual"">");
        twimlBuilder.AppendLine($@"<Number>{customer}</Number>");
        twimlBuilder.AppendLine("</Dial>");

        // Alternate customer (if provided)
        if (!string.IsNullOrWhiteSpace(alternate))
        {
            twimlBuilder.AppendLine(@"<Say>The primary number did not answer. Trying alternate number now.</Say>");
            twimlBuilder.AppendLine($@"<Dial callerId=""{_cfg.FromNumber}"" timeout=""20"" record=""record-from-answer-dual"">");
            twimlBuilder.AppendLine($@"<Number>{alternate}</Number>");
            twimlBuilder.AppendLine("</Dial>");
        }

        // Closing message
        twimlBuilder.AppendLine("<Say>Sorry, no one is available. Goodbye.</Say>");
        twimlBuilder.AppendLine("</Response>");

        var twiml = twimlBuilder.ToString();

        // all the agent first and let Twilio bridge to customer(s)C
        var call = await CallResource.CreateAsync(
            to: new PhoneNumber(agent),
            from: new PhoneNumber(_cfg.FromNumber),
            twiml: new Twiml(twiml)
        );

        CallSid = call.Sid;
        context.SetVariable("CallSid", CallSid);

        return Done();
    }

    private static string NormaliseIndianNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Phone number is empty.", nameof(raw));

        var digits = NonDigitsRegex.Replace(raw, "");

        if (digits.StartsWith('0'))
            digits = digits[1..];

        if (digits.Length == 10)
            digits = "91" + digits;
        else if (digits.Length == 12 && digits.StartsWith("91"))
        { /* ok — already has country code */ }
        else
            throw new FormatException("Phone must be 10 digits or 12 with country code 91.");

        return "+" + digits;
    }
}
