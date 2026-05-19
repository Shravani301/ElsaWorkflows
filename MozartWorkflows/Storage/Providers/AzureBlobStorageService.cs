using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using MozartWorkflows.Storage.Abstractions;
using MozartWorkflows.Storage.Entities;


namespace MozartWorkflows.Storage.Providers
{
    public sealed class AzureBlobStorageService : IStorageService
    {
        private readonly BlobContainerClient _container;

        public AzureBlobStorageService(StorageProviderConfig cfg)
        {
            var conn = cfg.ConnectionString ?? throw new ArgumentException("ConnectionString is required.", nameof(cfg));
            _container = new BlobContainerClient(conn, cfg.ContainerName!);
        }

        // ── Streaming-first upload ────────────────────────────────────────────────
        public async Task<string> UploadAsync(Stream data, string fileName, string contentType, string keyPrefix)
        {
            var blobName = $"{keyPrefix}/{fileName}";
            var blob = _container.GetBlobClient(blobName);

            var opts = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                // Tuning to cap memory usage for large files:
                TransferOptions = new StorageTransferOptions
                {
                    MaximumConcurrency = 4,
                    InitialTransferSize = 8 * 1024 * 1024, // 8 MiB
                    MaximumTransferSize = 8 * 1024 * 1024  // 8 MiB chunks
                }
            };

            await blob.UploadAsync(data, opts).ConfigureAwait(false);
            return blob.Uri.ToString();
        }

        public async Task<string> UploadAsync(byte[] data, string fileName, string contentType, string keyPrefix)
        {
            using var ms = new MemoryStream(data, writable: false);
            return await UploadAsync(ms, fileName, contentType, keyPrefix).ConfigureAwait(false);
        }

        // ── Streaming-first download (returns a stream; caller must Dispose) ─────
        public async Task<Stream> OpenReadAsync(string url)
        {
            // Reconstruct a blob client pointing at the same container, using the blob name from the URL.
            var uri = new Uri(url);
            // Extract the path after /{container}/
            var prefix = $"/{_container.Name}/";
            var blobName = uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? uri.AbsolutePath.Substring(prefix.Length)
                : uri.AbsolutePath.TrimStart('/'); // fallback

            var blob = _container.GetBlobClient(blobName);
            // OpenReadAsync streams directly from the service
            return await blob.OpenReadAsync().ConfigureAwait(false);
        }

        public async Task<byte[]> DownloadAsync(string url)
        {
            await using var s = await OpenReadAsync(url).ConfigureAwait(false);
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms).ConfigureAwait(false);
            return ms.ToArray();
        }
    }
}
