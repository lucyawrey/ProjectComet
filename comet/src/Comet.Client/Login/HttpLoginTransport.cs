using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Comet.Client.Login
{
    /// <summary>The Login server over <see cref="HttpClient"/>, for bots and tools (the web build uses UnityWebRequest).</summary>
    public sealed class HttpLoginTransport : ILoginTransport
    {
        private readonly HttpClient _http;

        /// <param name="http">A client whose <see cref="HttpClient.BaseAddress"/> is the Login server.</param>
        public HttpLoginTransport(HttpClient http) => _http = http;

        public async Task<LoginHttpResponse> SendAsync(string method, string path, string? json, string? bearerToken)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (json != null)
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            if (bearerToken != null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            }

            try
            {
                using var response = await _http.SendAsync(request).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new LoginHttpResponse((int)response.StatusCode, body);
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
            {
                return new LoginHttpResponse(0, e.Message);
            }
        }
    }
}
