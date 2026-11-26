using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FastCraft.Core
{
    /// <summary>
    /// One HTTP answer as the sign-in needs it: the status, the body, and how long the server
    /// asked us to wait (Retry-After). Mojang sends Retry-After when it throttles sign-in.
    /// </summary>
    public readonly struct HttpResult
    {
        public HttpResult(HttpStatusCode status, string body, double retryAfterSeconds, string diagnostics)
        {
            Status = status;
            Body = body ?? "";
            RetryAfterSeconds = retryAfterSeconds;
            Diagnostics = diagnostics ?? "";
        }

        public readonly HttpStatusCode Status;
        public readonly string Body;
        public readonly double RetryAfterSeconds;
        public readonly string Diagnostics;

        public int Code { get { return (int)Status; } }
        public bool IsOk { get { return Status == HttpStatusCode.OK; } }
        public bool IsRateLimited { get { return (int)Status == 429; } }
        public bool IsUnauthorized { get { return Status == HttpStatusCode.Unauthorized; } }
    }

    public static class Http
    {
        public static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 64,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                ConnectTimeout = TimeSpan.FromSeconds(15)
            };
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromMinutes(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FastCraft/1.0");
            return client;
        }

        public static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            using (var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                {
                    return await JsonDocument.ParseAsync(stream, default(JsonDocumentOptions), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        public static async Task<HttpResult> GetAsync(string url, string bearerToken, CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (!string.IsNullOrEmpty(bearerToken))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
                }
                request.Headers.Accept.ParseAdd("application/json");
                using (var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return new HttpResult(response.StatusCode, body, ReadRetryAfter(response), ReadDiagnostics(response));
                }
            }
        }

        public static async Task<HttpResult> PostJsonAsync(string url, string json, CancellationToken cancellationToken)
        {
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content })
            {
                // Xbox Live wants the contract version on its JSON endpoints; without it the
                // returned tokens can be ones the Minecraft API then rejects.
                request.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");
                request.Headers.Accept.ParseAdd("application/json");
                using (var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return new HttpResult(response.StatusCode, body, ReadRetryAfter(response), ReadDiagnostics(response));
                }
            }
        }

        public static async Task<HttpResult> PostFormAsync(string url, string form, CancellationToken cancellationToken)
        {
            using (var content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded"))
            using (var response = await Client.PostAsync(url, content, cancellationToken).ConfigureAwait(false))
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return new HttpResult(response.StatusCode, body, ReadRetryAfter(response), ReadDiagnostics(response));
            }
        }

        private static double ReadRetryAfter(HttpResponseMessage response)
        {
            var header = response.Headers.RetryAfter;
            if (header != null)
            {
                if (header.Delta.HasValue) return header.Delta.Value.TotalSeconds;
                if (header.Date.HasValue) return Math.Max(0.0, (header.Date.Value - DateTimeOffset.UtcNow).TotalSeconds);
            }
            return 0;
        }

        /// <summary>
        /// Response headers that explain why a request was refused (WWW-Authenticate,
        /// x-ms-error-code, …). Only filled for failures. No tokens appear here: those are sent
        /// in the request's Authorization header, not in a response.
        /// </summary>
        private static string ReadDiagnostics(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return "";
            var builder = new StringBuilder();
            AppendHeader(builder, response, "WWW-Authenticate");
            AppendHeader(builder, response, "x-ms-error-code");
            AppendHeader(builder, response, "x-ms-request-id");
            AppendHeader(builder, response, "x-ms-correlation-request-id");
            AppendHeader(builder, response, "server");
            if (builder.Length > 300) builder.Length = 300;
            return builder.ToString();
        }

        private static void AppendHeader(StringBuilder builder, HttpResponseMessage response, string name)
        {
            IEnumerable<string> found;
            List<string> values = null;
            if (response.Headers.TryGetValues(name, out found)) values = new List<string>(found);
            else if (response.Content.Headers.TryGetValues(name, out found)) values = new List<string>(found);
            if (values == null || values.Count == 0) return;
            if (builder.Length > 0) builder.Append("; ");
            builder.Append(name).Append("=").Append(string.Join(", ", values));
        }
    }
}
