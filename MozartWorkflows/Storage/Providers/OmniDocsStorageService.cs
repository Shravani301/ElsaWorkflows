using System.Net.Http.Headers;
using MozartWorkflows.Storage.Abstractions;
using MozartWorkflows.Storage.Entities;

namespace MozartWorkflows.Storage.Providers
{
    public sealed class OmniDocsStorageService : IStorageService
    {
        private readonly HttpClient _http;
        private readonly string _cabinet;

        public OmniDocsStorageService(StorageProviderConfig cfg)
        {
            _cabinet = cfg.OmniCabinet ?? throw new ArgumentException("OmniCabinet is required.", nameof(cfg));
            var url = cfg.OmniUrl ?? throw new ArgumentException("OmniUrl is required.", nameof(cfg));
            var user = cfg.OmniUsername ?? throw new ArgumentException("OmniUsername is required.", nameof(cfg));
            var pass = cfg.OmniPassword ?? throw new ArgumentException("OmniPassword is required.", nameof(cfg));

            _http = new HttpClient { BaseAddress = new Uri(url) };

            // Basic-auth; switch to OAuth/token if server supports it.
            var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{pass}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Streaming-first UPLOAD
        // Sends multipart/form-data with a StreamContent to avoid buffering whole file in memory.
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<string> UploadAsync(Stream data, string fileName, string contentType, string keyPrefix)
        {
            using var content = new MultipartFormDataContent();

            var fileContent = new StreamContent(data);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            // The Add(...) overload sets Content-Disposition with name and filename.
            content.Add(fileContent, "file", fileName);
            content.Add(new StringContent(_cabinet), "cabinet");
            content.Add(new StringContent(keyPrefix), "folder");

            using var res = await _http.PostAsync("/api/docs/upload", content).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();

            // Response body expected to be the new document ID (adjust if API differs).
            var docId = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            return $"{_http.BaseAddress}api/docs/{docId}";
        }

        public async Task<string> UploadAsync(byte[] data, string fileName, string contentType, string keyPrefix)
        {
            using var ms = new MemoryStream(data, writable: false);
            return await UploadAsync(ms, fileName, contentType, keyPrefix).ConfigureAwait(false);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Streaming-first DOWNLOAD
        // To keep memory usage bounded and avoid tying response lifetime to this method,
        // we stream the HTTP response to a temp file and return a FileStream that
        // deletes itself on Dispose.
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<Stream> OpenReadAsync(string url)
        {
            using var res = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();

            var tempPath = Path.Combine(Path.GetTempPath(), $"omni-{Guid.NewGuid():N}.bin");
            await using (var fs = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await res.Content.CopyToAsync(fs).ConfigureAwait(false);
            }

            // Return a stream that deletes the temp file when disposed.
            return new DeletingFileStream(tempPath);
        }

        public async Task<byte[]> DownloadAsync(string url)
        {
            await using var s = await OpenReadAsync(url).ConfigureAwait(false);
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms).ConfigureAwait(false);
            return ms.ToArray();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helper stream that deletes its underlying temp file on dispose.
        // ─────────────────────────────────────────────────────────────────────────
        private sealed class DeletingFileStream : FileStream
        {
            private readonly string _path;
            private bool _deleted;

            public DeletingFileStream(string path)
                : base(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                      FileOptions.Asynchronous | FileOptions.SequentialScan)
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
