using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Emberfall.Infrastructure.Content
{
    public interface IContentPackageTransport
    {
        Task<byte[]> DownloadAsync(Uri uri, long maximumBytes, CancellationToken cancellationToken);
    }

    public sealed class ContentPackageDownloadException : Exception
    {
        public ContentPackageDownloadException(string message) : base(message)
        {
        }

        public ContentPackageDownloadException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    public sealed class HttpContentPackageTransport : IContentPackageTransport, IDisposable
    {
        private readonly HttpClient _client;
        private readonly TimeSpan _timeout;
        private readonly bool _allowLoopbackHttp;

        public HttpContentPackageTransport(TimeSpan timeout, bool allowLoopbackHttp)
        {
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            _timeout = timeout;
            _allowLoopbackHttp = allowLoopbackHttp;
            _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        }

        public async Task<byte[]> DownloadAsync(Uri uri, long maximumBytes, CancellationToken cancellationToken)
        {
            ValidateUri(uri);
            if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            try
            {
                using HttpResponseMessage response = await _client.GetAsync(
                    uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new ContentPackageDownloadException(
                        $"Content endpoint returned HTTP {(int)response.StatusCode} for '{uri.AbsolutePath}'.");

                long? declaredLength = response.Content.Headers.ContentLength;
                if (declaredLength.HasValue && declaredLength.Value > maximumBytes)
                    throw new ContentPackageDownloadException(
                        $"Content endpoint declared {declaredLength.Value} bytes, above the {maximumBytes} byte limit.");

                using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var output = new MemoryStream(
                    declaredLength.HasValue ? (int)Math.Min(declaredLength.Value, maximumBytes) : 4096);
                var buffer = new byte[8192];
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    if (output.Length + read > maximumBytes)
                        throw new ContentPackageDownloadException(
                            $"Downloaded content exceeded the {maximumBytes} byte limit.");
                    output.Write(buffer, 0, read);
                }

                if (output.Length == 0) throw new ContentPackageDownloadException("Content endpoint returned an empty response.");
                return output.ToArray();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ContentPackageDownloadException($"Content download timed out after {_timeout.TotalSeconds:0.##} seconds.");
            }
            catch (HttpRequestException exception)
            {
                throw new ContentPackageDownloadException("Content download failed.", exception);
            }
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        private void ValidateUri(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri) throw new ContentPackageDownloadException("Content URI must be absolute.");
            bool isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            bool isAllowedHttp = _allowLoopbackHttp && uri.IsLoopback &&
                                 string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
            if (!isHttps && !isAllowedHttp)
                throw new ContentPackageDownloadException("Content URI must use HTTPS; development HTTP is limited to loopback.");
            if (!string.IsNullOrEmpty(uri.UserInfo))
                throw new ContentPackageDownloadException("Content URI cannot contain embedded credentials.");
        }
    }
}
