using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using MozartWorkflows.Storage.Abstractions;
using MozartWorkflows.Storage.Entities;

namespace MozartWorkflows.Storage.Providers
{
    public sealed class GcsStorageService : IStorageService
    {
        private readonly StorageClient _client;
        private readonly string _bucket;

        public GcsStorageService(StorageProviderConfig cfg)
        {
            _bucket = cfg.GcpBucketName ?? throw new ArgumentException("GcpBucketName is required.", nameof(cfg));

            var credsJson = cfg.GcpJsonKey ?? throw new ArgumentException("GcpJsonKey is required.", nameof(cfg));
            var creds = GoogleCredential.FromJson(credsJson);
            _client = StorageClient.Create(creds);
        }

        // ── Streaming-first upload ────────────────────────────────────────────────
        public async Task<string> UploadAsync(Stream data, string fileName, string contentType, string keyPrefix)
        {
            var objName = $"{keyPrefix}/{fileName}";
            var options = new UploadObjectOptions
            {
                // Tune if needed. Default ~10MB; adjust to control memory/network.
                ChunkSize = 10 * 1024 * 1024
            };

            await _client.UploadObjectAsync(_bucket, objName, contentType, data, options).ConfigureAwait(false);
            return $"https://storage.googleapis.com/{_bucket}/{objName}";
        }

        public async Task<string> UploadAsync(byte[] data, string fileName, string contentType, string keyPrefix)
        {
            using var ms = new MemoryStream(data, writable: false);
            return await UploadAsync(ms, fileName, contentType, keyPrefix).ConfigureAwait(false);
        }

        // ── Streaming-first download (returns a stream; caller must Dispose) ─────
        // GCS client downloads to a destination stream; to avoid large in-memory buffers
        // we stage to a temp file and return a FileStream that deletes itself on Dispose.
        public async Task<Stream> OpenReadAsync(string url)
        {
            var objName = Uri.UnescapeDataString(new Uri(url).AbsolutePath.TrimStart('/'));

            var tempPath = Path.Combine(Path.GetTempPath(), $"gcs-{Guid.NewGuid():N}.bin");
            await using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                await _client.DownloadObjectAsync(_bucket, objName, fs).ConfigureAwait(false);
            }

            return new DeletingFileStream(tempPath);
        }

        public async Task<byte[]> DownloadAsync(string url)
        {
            await using var s = await OpenReadAsync(url).ConfigureAwait(false);
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms).ConfigureAwait(false);
            return ms.ToArray();
        }

        // Helper stream that deletes the temp file when disposed.
        private sealed class DeletingFileStream : FileStream
        {
            private readonly string _path;
            private bool _deleted;

            public DeletingFileStream(string path)
                : base(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan)
            {
                _path = path;
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (!_deleted)
                {
                    try { File.Delete(_path); } catch { /* best effort */ }
                    _deleted = true;
                }
            }
        }
    }
}
