namespace MozartWorkflows.Models
{
    public class UploadFileResponse
    {
        public bool UploadFileStatus { get; set; }
        public string? FilePath { get; set; }
        public int StatusCode { get; set; }
        public string? Message { get; set; }

    }
    public class DeleteFileResponse
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public string? Message { get; set; }
    }
}
