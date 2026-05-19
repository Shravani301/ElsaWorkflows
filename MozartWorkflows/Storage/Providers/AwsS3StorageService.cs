using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Transfer;
using MozartWorkflows.Storage.Abstractions;
using MozartWorkflows.Storage.Entities;

namespace MozartWorkflows.Storage.Providers
{
    public sealed class AwsS3StorageService : IStorageService
    {
        private readonly IAmazonS3 _s3;
        private readonly string _bucket;

        public AwsS3StorageService(StorageProviderConfig cfg)
        {
            _bucket = cfg.BucketName ?? throw new ArgumentException("BucketName is required.", nameof(cfg));
            var creds = new BasicAWSCredentials(cfg.AccessKey!, cfg.SecretKey!);
            _s3 = new AmazonS3Client(creds, RegionEndpoint.GetBySystemName(cfg.Region));
        }

        // ── Streaming-first upload ────────────────────────────────────────────────
        public async Task<string> UploadAsync(Stream data, string fileName, string contentType, string keyPrefix)
        {
            var key = $"{keyPrefix}/{fileName}";

            var req = new TransferUtilityUploadRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = data,
                ContentType = contentType,
                // Optional tuning for multipart:
                // PartSize = 8 * 1024 * 1024 // 8 MiB
            };

            var util = new TransferUtility(_s3);
            await util.UploadAsync(req).ConfigureAwait(false);
            return $"https://{_bucket}.s3.amazonaws.com/{key}";
        }

        public async Task<string> UploadAsync(byte[] data, string fileName, string contentType, string keyPrefix)
        {
            using var ms = new MemoryStream(data, writable: false);
            return await UploadAsync(ms, fileName, contentType, keyPrefix).ConfigureAwait(false);
        }

        // ── Streaming-first download (returns a stream; caller must Dispose) ─────
        public async Task<Stream> OpenReadAsync(string url)
        {
            var key = Uri.UnescapeDataString(new Uri(url).AbsolutePath.TrimStart('/'));
            var response = await _s3.GetObjectAsync(_bucket, key).ConfigureAwait(false);
            // Do not dispose 'response' here; disposing the returned stream will close it.
            return response.ResponseStream;
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
