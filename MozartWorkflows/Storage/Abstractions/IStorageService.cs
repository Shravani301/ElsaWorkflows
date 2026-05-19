namespace MozartWorkflows.Storage.Abstractions
{
    public interface IStorageService
    {
        // Preferred, streaming-first APIs (large-file friendly)
        Task<string> UploadAsync(Stream data, string fileName, string contentType, string keyPrefix);
        Task<string> UploadAsync(byte[] data, string fileName, string contentType, string keyPrefix);
        Task<Stream> OpenReadAsync(string url);

        // Back-compat shims (delegate to streaming APIs)
        Task<byte[]> DownloadAsync(string url);
    }
}
