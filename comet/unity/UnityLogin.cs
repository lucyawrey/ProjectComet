using System.Text;
using System.Threading.Tasks;
using Comet.Client.Login;
using UnityEngine;
using UnityEngine.Networking;

namespace Comet.Unity
{
    /// <summary>
    /// The Login server over <see cref="UnityWebRequest"/>, which works in web builds (the browser's fetch) as well as on
    /// desktop. Await it on Unity's main thread.
    /// </summary>
    public sealed class UnityLoginTransport : ILoginTransport
    {
        private const int TimeoutSeconds = 15;

        private readonly string _baseUrl;

        /// <param name="baseUrl">The Login server, such as <c>https://server.example.com</c> (paths start with /api/).</param>
        public UnityLoginTransport(string baseUrl) => _baseUrl = baseUrl.TrimEnd('/');

        public Task<LoginHttpResponse> SendAsync(string method, string path, string? json, string? bearerToken)
        {
            var request = new UnityWebRequest(_baseUrl + path, method) { downloadHandler = new DownloadHandlerBuffer(), timeout = TimeoutSeconds };
            if (json != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            if (bearerToken != null)
            {
                request.SetRequestHeader("Authorization", "Bearer " + bearerToken);
            }

            var done = new TaskCompletionSource<LoginHttpResponse>();
            request.SendWebRequest().completed += _ =>
            {
                // No answer at all (refused, unreachable, timed out, blocked) is status 0.
                var answered = request.result != UnityWebRequest.Result.ConnectionError && request.responseCode > 0;
                done.SetResult(new LoginHttpResponse(answered ? (int)request.responseCode : 0, request.downloadHandler?.text ?? ""));
                request.Dispose();
            };
            return done.Task;
        }
    }

    /// <summary>
    /// Keeps the session token in PlayerPrefs: IndexedDB in a web build, the usual per-user settings store on desktop.
    /// </summary>
    public sealed class PlayerPrefsTokenStore : ITokenStore
    {
        private readonly string _key;

        public PlayerPrefsTokenStore(string key) => _key = key;

        public string? Load()
        {
            var token = PlayerPrefs.GetString(_key, "");
            return token.Length > 0 ? token : null;
        }

        public void Save(string? token)
        {
            if (token == null)
            {
                PlayerPrefs.DeleteKey(_key);
            }
            else
            {
                PlayerPrefs.SetString(_key, token);
            }

            PlayerPrefs.Save(); // at once: a web page may close any moment
        }
    }
}
