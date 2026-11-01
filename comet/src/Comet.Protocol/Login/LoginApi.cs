namespace Comet.Protocol.Login
{
    /// <summary>
    /// The Login server's HTTP interface: JSON (camelCase) under <c>/api/</c>. Signed-in calls send the session token
    /// as <c>Authorization: Bearer &lt;token&gt;</c>. Errors are Problem Details (RFC 9457) whose <c>code</c> is one of
    /// <see cref="LoginErrors"/>. The game's own character data (<typeparamref name="TCharacter"/> below) is the
    /// game's type.
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

    /// <summary>The stable error codes in the Login server's Problem Details, mapped to interface text by clients.</summary>
    public static class LoginErrors
    {
        /// <summary>The session token is missing, expired or revoked: forget it.</summary>
        public const string SignedOut = "signed_out";

        public const string NameTaken = "name_taken";
        public const string InvalidName = "invalid_name";

        /// <summary>The game refused the character in a new guest's request.</summary>
        public const string InvalidCharacter = "invalid_character";

        /// <summary>The username or password is wrong (which one isn't said).</summary>
        public const string WrongPassword = "wrong_password";

        public const string WrongCode = "wrong_code";

        /// <summary>Too many wrong passwords from this address for this account: try later, or use a recovery code.</summary>
        public const string LockedOut = "locked_out";

        public const string WeakPassword = "weak_password";
        public const string Banned = "banned";

        /// <summary>The account is deleted and can be restored (<see cref="LoginPaths.Restore"/>) until it's purged.</summary>
        public const string AccountDeleted = "account_deleted";

        /// <summary>The session is too old for this change: sign in again first.</summary>
        public const string SignInAgain = "sign_in_again";

        /// <summary>Only a claimed account (one with a password) can do this.</summary>
        public const string NotClaimed = "not_claimed";

        public const string NoCharacter = "no_character";
        public const string RateLimited = "rate_limited";
    }

    /// <summary>Makes a guest account and its first character.</summary>
    public sealed class GuestRequest<TCharacter>
    {
        public string Name { get; set; } = "";
        public TCharacter? Character { get; set; }
    }

    /// <summary>Signs in with a username and password, or restores a deleted account with them.</summary>
    public sealed class SignInRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";

        /// <summary>For <see cref="LoginPaths.Restore"/>: a recovery code instead of the username and password.</summary>
        public string? RecoveryCode { get; set; }
    }

    public sealed class RecoveryCodeSignInRequest
    {
        public string Code { get; set; } = "";
    }

    /// <summary>A new session: the token to keep and send.</summary>
    public sealed class SessionResponse
    {
        public string Token { get; set; } = "";
    }

    public sealed class PasswordRequest
    {
        public string Password { get; set; } = "";
    }

    /// <summary>The new recovery code, shown once. It's never stored in the clear and can't be shown again.</summary>
    public sealed class RecoveryCodeResponse
    {
        public string Code { get; set; } = "";
    }

    public sealed class RenameRequest
    {
        public string Name { get; set; } = "";
    }

    /// <summary>The signed-in account, and its character (if it has one).</summary>
    public sealed class AccountResponse<TCharacter>
    {
        public string Username { get; set; } = "";

        /// <summary>No password set yet: the account lives only on this device.</summary>
        public bool Guest { get; set; }

        public bool HasRecoveryCode { get; set; }

        /// <summary>The session is recent enough to change the password or make a recovery code without signing in again.</summary>
        public bool RecentSignIn { get; set; }

        public TCharacter? Character { get; set; }
    }

    /// <summary>A join ticket and the game server it's for.</summary>
    public sealed class TicketResponse
    {
        public string Ticket { get; set; } = "";

        /// <summary>The game server's WebSocket address.</summary>
        public string Server { get; set; } = "";
    }
}
