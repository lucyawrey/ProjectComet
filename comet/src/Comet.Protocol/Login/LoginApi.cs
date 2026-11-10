namespace Comet.Protocol.Login
{
    /// <summary>
    /// The Login server's HTTP interface: JSON (camelCase) under <c>/api/</c>, session token as <c>Authorization: Bearer &lt;token&gt;</c>.
    /// Errors are Problem Details (RFC 9457) whose <c>code</c> is one of <see cref="LoginErrors"/>.
    /// </summary>
    public static class LoginPaths
    {
        public const string Guest = "/api/guest";
        public const string SignIn = "/api/sign-in";
        public const string SignInWithCode = "/api/sign-in/recovery-code";
        public const string Restore = "/api/restore";
        public const string Password = "/api/account/password";
        public const string RecoveryCode = "/api/account/recovery-code";
        public const string SignOut = "/api/account/sign-out";
        public const string Me = "/api/account";
        public const string Rename = "/api/account/rename";
        public const string Ticket = "/api/ticket";
    }

    public static class LoginErrors
    {
        public const string SignedOut = "signed_out";

        public const string NameTaken = "name_taken";
        public const string InvalidName = "invalid_name";

        public const string InvalidCharacter = "invalid_character";

        public const string WrongPassword = "wrong_password";

        public const string WrongCode = "wrong_code";

        public const string LockedOut = "locked_out";

        public const string WeakPassword = "weak_password";
        public const string Banned = "banned";

        public const string AccountDeleted = "account_deleted";

        public const string SignInAgain = "sign_in_again";

        public const string NotClaimed = "not_claimed";

        public const string NoCharacter = "no_character";
        public const string RateLimited = "rate_limited";
    }

    public sealed class GuestRequest<TCharacter>
    {
        public string Name { get; set; } = "";
        public TCharacter? Character { get; set; }
    }

    public sealed class SignInRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";

        public string? RecoveryCode { get; set; }
    }

    public sealed class RecoveryCodeSignInRequest
    {
        public string Code { get; set; } = "";
    }

    public sealed class SessionResponse
    {
        public string Token { get; set; } = "";
    }

    public sealed class PasswordRequest
    {
        public string Password { get; set; } = "";
    }

    public sealed class RecoveryCodeResponse
    {
        public string Code { get; set; } = "";
    }

    public sealed class RenameRequest
    {
        public string Name { get; set; } = "";
    }

    public sealed class AccountResponse<TCharacter>
    {
        public string Username { get; set; } = "";

        public bool Guest { get; set; }

        public bool HasRecoveryCode { get; set; }

        public bool RecentSignIn { get; set; }

        public TCharacter? Character { get; set; }
    }

    public sealed class TicketResponse
    {
        public string Ticket { get; set; } = "";

        /// <summary>The game server's WebSocket address.</summary>
        public string Server { get; set; } = "";
    }
}
