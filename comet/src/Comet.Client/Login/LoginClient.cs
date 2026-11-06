using System.Text.Json;
using System.Threading.Tasks;
using Comet.Protocol.Login;

namespace Comet.Client.Login
{
    /// <summary>An HTTP response from the Login server, or a network failure (<see cref="Status"/> 0).</summary>
    public readonly struct LoginHttpResponse
    {
        public LoginHttpResponse(int status, string body)
        {
            Status = status;
            Body = body;
        }

        public int Status { get; }
        public string Body { get; }
        public bool Succeeded => Status >= 200 && Status < 300;
    }

    /// <summary>
    /// Sends HTTP requests to the Login server: <c>UnityWebRequest</c> in the game (which works on web), plain
    /// <c>HttpClient</c> for the bots (<see cref="HttpLoginTransport"/>).
    /// </summary>
    public interface ILoginTransport
    {
        /// <summary>
        /// Sends a request with an optional JSON body and bearer token. A network failure (no answer at all) returns
        /// status 0 instead of throwing.
        /// </summary>
        Task<LoginHttpResponse> SendAsync(string method, string path, string? json, string? bearerToken);
    }

    /// <summary>Where the session token is kept between visits (PlayerPrefs in the game, a file for bots).</summary>
    public interface ITokenStore
    {
        string? Load();

        /// <summary>Keeps the token, or forgets it when null.</summary>
        void Save(string? token);
    }

    /// <summary>A Login server call's result: the value, or an error code (<see cref="LoginErrors"/> or <see cref="LoginClient{TCharacter}.Unreachable"/>).</summary>
    public readonly struct LoginResult<T>
    {
        public LoginResult(T value)
        {
            Value = value;
            Error = null;
        }

        private LoginResult(string error)
        {
            Value = default;
            Error = error;
        }

        public T? Value { get; }
        public string? Error { get; }
        public bool Succeeded => Error == null;

        public static LoginResult<T> Failed(string error) => new LoginResult<T>(error);
    }

    /// <summary>
    /// The Login server's client: keeps the session token (<see cref="ITokenStore"/>), sends it with signed-in calls,
    /// and forgets it when the server says it's no longer good (<see cref="LoginErrors.SignedOut"/>).
    /// <typeparamref name="TCharacter"/> is how the game's Login server describes a character.
    /// </summary>
    public sealed class LoginClient<TCharacter>
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly ILoginTransport _transport;
        private readonly ITokenStore _tokens;

        public LoginClient(ILoginTransport transport, ITokenStore tokens)
        {
            _transport = transport;
            _tokens = tokens;
            Token = tokens.Load();
        }

        /// <summary>The session token, or null when signed out.</summary>
        public string? Token { get; private set; }

        public bool SignedIn => Token != null;

        /// <summary>Makes a guest account with its first character, from the game's new-character data.</summary>
        public Task<LoginResult<bool>> CreateGuestAsync<TNewCharacter>(string name, TNewCharacter character) =>
            StartSessionAsync(LoginPaths.Guest, new GuestRequest<TNewCharacter> { Name = name, Character = character });

        public Task<LoginResult<bool>> SignInAsync(string username, string password) =>
            StartSessionAsync(LoginPaths.SignIn, new SignInRequest { Username = username, Password = password });

        /// <summary>
        /// Signs in again (for a change that needs a recent sign-in), replacing this device's session rather than
        /// adding one: the old session is revoked, best effort, once the new one is kept.
        /// </summary>
        public async Task<LoginResult<bool>> SignInAgainAsync(string username, string password)
        {
            var old = Token;
            var result = await SignInAsync(username, password);
            if (result.Succeeded && old != null)
            {
                await _transport.SendAsync("POST", LoginPaths.SignOut, null, old);
            }

            return result;
        }

        public Task<LoginResult<bool>> SignInWithCodeAsync(string code) =>
            StartSessionAsync(LoginPaths.SignInWithCode, new RecoveryCodeSignInRequest { Code = code });

        /// <summary>Restores a deleted account (after a sign-in said <see cref="LoginErrors.AccountDeleted"/>) and signs in.</summary>
        public Task<LoginResult<bool>> RestoreAsync(string username, string password, string? recoveryCode = null) =>
            StartSessionAsync(
                LoginPaths.Restore,
                new SignInRequest
                {
                    Username = username,
                    Password = password,
                    RecoveryCode = recoveryCode,
                }
            );

        public Task<LoginResult<AccountResponse<TCharacter>>> GetAccountAsync() => SendAsync<AccountResponse<TCharacter>>("GET", LoginPaths.Me, null);

        public Task<LoginResult<bool>> SetPasswordAsync(string password) =>
            SendAsync<bool>("POST", LoginPaths.Password, new PasswordRequest { Password = password });

        public Task<LoginResult<RecoveryCodeResponse>> MakeRecoveryCodeAsync() =>
            SendAsync<RecoveryCodeResponse>("POST", LoginPaths.RecoveryCode, null);

        public Task<LoginResult<bool>> RenameAsync(string name) => SendAsync<bool>("POST", LoginPaths.Rename, new RenameRequest { Name = name });

        public Task<LoginResult<TicketResponse>> GetTicketAsync() => SendAsync<TicketResponse>("POST", LoginPaths.Ticket, null);

        /// <summary>Signs out on the server (best effort) and forgets the token either way.</summary>
        public async Task SignOutAsync()
        {
            if (Token != null)
            {
                await SendAsync<bool>("POST", LoginPaths.SignOut, null);
            }

            Forget();
        }

        /// <summary>Forgets the token without telling the server.</summary>
        public void Forget()
        {
            Token = null;
            _tokens.Save(null);
        }

        /// <summary>The error code for a request that got no answer.</summary>
        public const string Unreachable = "unreachable";

        private async Task<LoginResult<bool>> StartSessionAsync(string path, object request)
        {
            var result = await SendAsync<SessionResponse>("POST", path, request, withToken: false);
            if (!result.Succeeded)
            {
                return LoginResult<bool>.Failed(result.Error!);
            }

            Token = result.Value!.Token;
            _tokens.Save(Token);
            return new LoginResult<bool>(true);
        }

        private async Task<LoginResult<T>> SendAsync<T>(string method, string path, object? body, bool withToken = true)
        {
            if (withToken && Token == null)
            {
                return LoginResult<T>.Failed(LoginErrors.SignedOut);
            }

            var json = body == null ? null : JsonSerializer.Serialize(body, body.GetType(), Json);
            var response = await _transport.SendAsync(method, path, json, withToken ? Token : null);
            if (response.Status == 0)
            {
                return LoginResult<T>.Failed(Unreachable);
            }

            if (!response.Succeeded)
            {
                var error = ErrorCode(response);
                if (error == LoginErrors.SignedOut && withToken)
                {
                    Forget();
                }

                return LoginResult<T>.Failed(error);
            }

            if (typeof(T) == typeof(bool))
            {
                return new LoginResult<T>((T)(object)true);
            }

            try
            {
                return new LoginResult<T>(JsonSerializer.Deserialize<T>(response.Body, Json)!);
            }
            catch (JsonException)
            {
                return LoginResult<T>.Failed(Unreachable);
            }
        }

        // The Problem Details' code, or a generic one for an answer that isn't ours (a proxy's error page).
        private static string ErrorCode(LoginHttpResponse response)
        {
            try
            {
                using var problem = JsonDocument.Parse(response.Body);
                if (
                    problem.RootElement.ValueKind == JsonValueKind.Object
                    && problem.RootElement.TryGetProperty("code", out var code)
                    && code.ValueKind == JsonValueKind.String
                )
                {
                    return code.GetString()!;
                }
            }
            catch (JsonException) { }

            return response.Status == 429 ? LoginErrors.RateLimited : Unreachable;
        }
    }
}
