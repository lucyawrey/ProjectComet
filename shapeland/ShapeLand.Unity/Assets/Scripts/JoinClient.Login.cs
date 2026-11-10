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

        public static void ForgetToken(string loginAddress) => PlayerPrefs.DeleteKey(TokenKeyFor(LoginUrl(loginAddress)));

        private static string TokenKeyFor(string loginUrl) => $"{TokenKey} {loginUrl}";

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

        public string LoginAddress => _loginAddress;

        public static string LoginUrl(string loginAddress)
        {
            loginAddress = (loginAddress ?? "").Trim().TrimEnd('/');
            return loginAddress.Contains("://") ? loginAddress : "http://" + loginAddress;
        }

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

        public async Task<string> ResumeAsync()
        {
            var attempt = _joinAttempt;
            var error = await LoadAccountAsync();
            return error != null || attempt != _joinAttempt ? error : await PlayAsync();
        }

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

        public async Task<string> CreateGuestAsync(string name, NewCharacter character)
        {
            var made = await _login.CreateGuestAsync(name, character);
            return made.Succeeded ? await ResumeAsync() : made.Error;
        }

        public async Task<string> SignInAsync(string username, string password)
        {
            var signedIn = await _login.SignInAsync(username, password);
            return signedIn.Succeeded ? await ResumeAsync() : signedIn.Error;
        }

        public async Task<string> SignInWithCodeAsync(string code)
        {
            var signedIn = await _login.SignInWithCodeAsync(code);
            return signedIn.Succeeded ? await ResumeAsync() : signedIn.Error;
        }

        public async Task<string> SetPasswordAsync(string password)
        {
            var set = await _login.SetPasswordAsync(password);
            await RefreshAccountAsync();
            return set.Error;
        }

        public async Task<(string Code, string Error)> MakeRecoveryCodeAsync()
        {
            var made = await _login.MakeRecoveryCodeAsync();
            await RefreshAccountAsync();
            return (made.Value?.Code, made.Error);
        }

        public async Task<string> ConfirmPasswordAsync(string password)
        {
            var signedIn = await _login.SignInAgainAsync(PlayerName, password);
            await RefreshAccountAsync();
            return signedIn.Error;
        }

        public async Task SignOutAsync()
        {
            Leave();
            await _login.SignOutAsync();
            Account = null;
            SignedOut?.Invoke();
        }

        public async Task RefreshAccountAsync()
        {
            var account = await _login.GetAccountAsync();
            if (account.Succeeded)
            {
                Account = account.Value;
            }
        }

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
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern string ShapeLand_PageLogin();
#endif
    }
}
