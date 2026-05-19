namespace MozartWorkflows.PhoneCall.Models
{
    public class TwilioSettings
    {
        public string AccountSid { get; set; } = default!;
        public string AuthToken { get; set; } = default!;
        public string FromNumber { get; set; } = default!;
        public string VoiceUrl { get; set; } = default!;
    }
}
