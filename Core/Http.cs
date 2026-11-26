using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FastCraft.Core
{
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

        public static async Task<(HttpStatusCode Status, string Body)> GetAsync(string url, string bearerToken, CancellationToken cancellationToken)
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
                    return (response.StatusCode, body);
                }
            }
        }

        public static async Task<(HttpStatusCode Status, string Body)> PostJsonAsync(string url, string json, CancellationToken cancellationToken)
        {
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var response = await Client.PostAsync(url, content, cancellationToken).ConfigureAwait(false))
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return (response.StatusCode, body);
            }
        }

        public static async Task<(HttpStatusCode Status, string Body)> PostFormAsync(string url, string form, CancellationToken cancellationToken)
        {
            using (var content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded"))
            using (var response = await Client.PostAsync(url, content, cancellationToken).ConfigureAwait(false))
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return (response.StatusCode, body);
            }
        }
    }
}
