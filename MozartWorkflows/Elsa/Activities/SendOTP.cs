using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using Org.BouncyCastle.Asn1.Crmf;
using RabbitMQ.Client;
using RestSharp;

namespace MozartWorkflows.Elsa.Activities
{
    [Activity(
        Category = "OTP",
        DisplayName = "Send OTP via Gupshup - Mobile Number",
        Description = "Generates an OTP and sends it to the provided mobile number using Gupshup SMS."
    )]
    public class SendOtpActivity : Activity
    {
        private readonly IConfiguration _configuration;

        public SendOtpActivity(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [ActivityInput(Hint = "Mobile number to send the OTP to (without country code if sender handles it)", SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Literal })]
        public string MobileNumber { get; set; } = default!;

        protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
        {
            if (string.IsNullOrEmpty(MobileNumber))
                return Fault("Mobile number is required.");

            Console.WriteLine($"Sending OTP to: {MobileNumber}");

            // Use the generated OTP from context
            var otp = context.GetVariable<string>("generatedOTP");
            Console.WriteLine($"Generated OTP: {otp}");

            // Load config
            var gupshupConfig = _configuration.GetSection("Gupshup:Sms");
            var username = gupshupConfig["Username"];
            var password = gupshupConfig["Password"];
            var senderId = gupshupConfig["SenderId"];
            var url = gupshupConfig["Url"];

            // Build message
            var message = $"Your OTP is {otp}. Please do not share it with anyone.";

            // Format number
            var formattedMobile = MobileNumber.StartsWith("+91") ? MobileNumber : "+91" + MobileNumber;

            // Build RestSharp client & request
            var client = new RestClient(url ?? string.Empty);
            var request = new RestRequest("", Method.Get);

            request.AddParameter("method", "SENDMESSAGE");
            request.AddParameter("send_to", formattedMobile);
            request.AddParameter("msg", message);
            request.AddParameter("msg_type", "text");
            request.AddParameter("userid", username);
            request.AddParameter("auth_scheme", "PLAIN");
            request.AddParameter("password", password);
            request.AddParameter("v", "1.1");
            request.AddParameter("format", "JSON");
            request.AddParameter("mask", senderId);

            try
            {
                var response = await client.ExecuteAsync(request);

                Console.WriteLine($"Gupshup status code: {response.StatusCode}");
                Console.WriteLine($"Gupshup response: {response.Content}");

                if (!response.IsSuccessful)
                    return Fault($"Failed to send OTP. Gupshup API responded with: {response.Content}");

                context.SetVariable("GeneratedOtp", otp);

                return Done();
            }
            catch (Exception ex)
            {
                return Fault($"Exception while sending OTP: {ex.Message}");
            }
        }
    }
}
