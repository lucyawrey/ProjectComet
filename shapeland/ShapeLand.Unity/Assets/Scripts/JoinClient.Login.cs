using System;
using System.Threading.Tasks;
using Comet.Client.Login;
using Comet.Protocol.Login;
using Comet.Unity;
using ShapeLand.Shared.Messages;
using UnityEngine;

namespace ShapeLand.Client
{
    public sealed partial class JoinClient
    {
        /// <summary>
        /// The start of the session tokens' PlayerPrefs keys, one per Login server, so a token is only ever sent to the
        /// server it came from. Tests use their own, so they never touch a player's.
        /// </summary>
        public static string TokenKey = "login-token";

        /// <summary>Forgets the token kept for a Login server (tests, so each starts as a new player and leaves nothing behind).</summary>
        public static void ForgetToken(string loginAddress) => PlayerPrefs.DeleteKey(TokenKeyFor(LoginUrl(loginAddress)));

        private static string TokenKeyFor(string loginUrl) => $"{TokenKey} {loginUrl}";

        /// <summary>Uses another Login server (a desktop build's server field), with the token kept for it.</summary>
        public void UseLoginServer(string loginAddress)
        {
            var url = LoginUrl(loginAddress);
            if (url == _loginAddress && _login != null)
            {
                return;
            }

            _loginAddress = url;
            _login = new LoginClient<CharacterView>(new UnityLoginTransport(url), new PlayerPrefsTokenStore(TokenKeyFor(url)));
        }

        /// <summary>The Login server's address in use.</summary>
        public string LoginAddress => _loginAddress;

        /// <summary>
        /// The Login server's URL for an address typed or given: a host and port (http://), or a full http(s):// URL.
        /// </summary>
        public static string LoginUrl(string loginAddress)
        {
            loginAddress = (loginAddress ?? "").Trim().TrimEnd('/');
            return loginAddress.Contains("://") ? loginAddress : "http://" + loginAddress;
        }

        /// <summary>
        /// A web build's Login server: the page's <c>?login=</c>, else <paramref name="pageLogin"/> (the one the page's
        /// deploy names), else <paramref name="fallback"/>.
        /// </summary>
        public static string LoginForPage(string pageUrl, string fallback, string pageLogin = null)
        {
            if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var page))
            {
                foreach (var pair in page.Query.TrimStart('?').Split('&'))
                {
                    if (pair.StartsWith("login=", StringComparison.Ordinal) && pair.Length > "login=".Length)
                    {
                        return Uri.UnescapeDataString(pair.Substring("login=".Length));
                    }
                }
            }

            return string.IsNullOrEmpty(pageLogin) ? fallback : pageLogin;
        }

        /// <summary>
        /// For a returning player (a kept token): loads the account and joins its character. Returns null once joining,
        /// <see cref="LoginErrors.SignedOut"/> when there's no usable token (show the join card), or another error code.
        /// </summary>
        public async Task<string> ResumeAsync()
        {
            var attempt = _joinAttempt;
            var error = await LoadAccountAsync();
            return error != null || attempt != _joinAttempt ? error : await PlayAsync();
        }

        /// <summary>
        /// Loads the signed-in account and its character without joining. Returns null, <see cref="LoginErrors.SignedOut"/>
        /// when there's no usable token, or another error code.
        /// </summary>
        public async Task<string> LoadAccountAsync()
        {
            if (!SignedIn)
            {
                return LoginErrors.SignedOut;
            }

            var account = await _login.GetAccountAsync();
            if (!account.Succeeded)
            {
                if (account.Error == LoginErrors.SignedOut)
                {
                    Account = null;
                    SignedOut?.Invoke();
                }

                return account.Error;
            }

            Account = account.Value;
            return null;
        }

        /// <summary>Makes a guest account with its first character, then joins. Returns an error code, or null.</summary>
        public async Task<string> CreateGuestAsync(string name, NewCharacter character)
        {
            var made = await _login.CreateGuestAsync(name, character);
            return made.Succeeded ? await ResumeAsync() : made.Error;
        }

        /// <summary>Signs in with a username and password, then joins. Returns an error code, or null.</summary>
        public async Task<string> SignInAsync(string username, string password)
        {
            var signedIn = await _login.SignInAsync(username, password);
            return signedIn.Succeeded ? await ResumeAsync() : signedIn.Error;
        }

        /// <summary>Signs in with a recovery code, then joins. Returns an error code, or null.</summary>
        public async Task<string> SignInWithCodeAsync(string code)
        {
            var signedIn = await _login.SignInWithCodeAsync(code);
            return signedIn.Succeeded ? await ResumeAsync() : signedIn.Error;
        }

        /// <summary>Sets the password (a guest's first claims the account; changing it needs a recent sign-in). Returns an error code, or null.</summary>
        public async Task<string> SetPasswordAsync(string password)
        {
            var set = await _login.SetPasswordAsync(password);
            await RefreshAccountAsync();
            return set.Error;
        }

        /// <summary>Makes a new recovery code (voiding the old), shown once. Returns it, or an error code.</summary>
        public async Task<(string Code, string Error)> MakeRecoveryCodeAsync()
        {
            var made = await _login.MakeRecoveryCodeAsync();
            await RefreshAccountAsync();
            return (made.Value?.Code, made.Error);
        }

        /// <summary>Signs in again with the password, for a change that needs a recent sign-in. Returns an error code, or null.</summary>
        public async Task<string> ConfirmPasswordAsync(string password)
        {
            var signedIn = await _login.SignInAgainAsync(PlayerName, password);
            await RefreshAccountAsync();
            return signedIn.Error;
        }

        /// <summary>Signs out: leaves the game, revokes the session and forgets the token.</summary>
        public async Task SignOutAsync()
        {
            Leave();
            await _login.SignOutAsync();
            Account = null;
            SignedOut?.Invoke();
        }

        /// <summary>Reloads the account (after a change), keeping the old one if the Login server can't be reached.</summary>
        public async Task RefreshAccountAsync()
        {
            var account = await _login.GetAccountAsync();
            if (account.Succeeded)
            {
                Account = account.Value;
            }
        }

        /// <summary>
        /// Renames the account at the Login server, then tells the game server, which shows everyone the new name.
        /// Returns an error code (<see cref="LoginErrors.NameTaken"/>, <see cref="LoginErrors.InvalidName"/>…), or null.
        /// </summary>
        public async Task<string> RenameAsync(string name)
        {
            var renamed = await _login.RenameAsync(name);
            if (!renamed.Succeeded)
            {
                return renamed.Error;
            }

            await RefreshAccountAsync();
            if (Joined && Session != null && !Session.Closed)
            {
                Session.Write(ShapeLandMessageIds.NameChanged, new NameChanged());
            }

            return null;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Plugins/WebGL/ShapeLandPage.jslib.
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern string ShapeLand_PageLogin();
#endif
    }
}
