using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Services.Models;
using Elsa;
using Elsa.Services;
using System;
using System.Security.Cryptography;

namespace MozartWorkflows.Elsa.Activities
{
    [Activity(
    Category = "Custom",
    DisplayName = "Generate OTP",
    Description = "Generates a six-digit OTP and stores it in workflow context."
)]
    public class GenerateOtpActivity : Activity
    {
        protected override IActivityExecutionResult OnExecute(ActivityExecutionContext context)
        {
            var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            // Store the OTP in the workflow context.
            context.SetVariable("GeneratedOtp", otp);
            Console.WriteLine($"[GenerateOtpActivity] OTP Generated: {otp}");

            // Optionally, if you need to access the recipient in this activity:
            var recipient = context.GetVariable<string>("Recipient");
            if (!string.IsNullOrWhiteSpace(recipient))
                Console.WriteLine($"[GenerateOtpActivity] Recipient: {recipient}");
            return Done();
        }
    }
}
