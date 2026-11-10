using System.Text.Json;
using Comet.Data;
using Comet.Data.Accounts;
using Comet.Data.Characters;
using Comet.Protocol.Login;
using Comet.Tickets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Comet.Login;

public sealed record LoginError(int Status, string Code);

public sealed record SignedIn(Account Account, Session Session);

public sealed class LoginAccounts<TCharacter>(
    CometAccountContext db,
    RegionContexts<TCharacter> regions,
    UserManager<Account> users,
    ILoginRules<TCharacter> rules,
    SignInLockout lockout,
    AccountActions<TCharacter> actions,
    TicketIssuer tickets,
    SnowflakeIds ids,
    IOptions<LoginOptions> options,
    TimeProvider clock
)
    where TCharacter : Character
{
    private const int MaxPasswordLength = 128;
    private static readonly TimeSpan SlideEvery = TimeSpan.FromHours(1);

    private LoginOptions Settings => options.Value;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<(string? Token, LoginError? Error)> CreateGuestAsync(
        string name,
        JsonElement characterRequest,
        CancellationToken cancellationToken
    )
    {
        if (rules.CheckName(name) is { } invalid)
        {
            return (null, new(StatusCodes.Status400BadRequest, invalid));
        }

        var account = new Account
        {
            Id = ids.Next(),
            UserName = name,
            LastActiveAt = Now,
        };
        var character = rules.CreateCharacter(ids.Next(), account.Id, name, characterRequest);
        if (character == null)
        {
            return (null, new(StatusCodes.Status400BadRequest, LoginErrors.InvalidCharacter));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var created = await users.CreateAsync(account);
            if (!created.Succeeded)
            {
                return (null, NameError(created));
            }

            await using var region = await regions.JoinAsync(db, cancellationToken);
            region.Characters.Add(character);
            await region.SaveChangesAsync(cancellationToken);

            var token = await NewSessionAsync(account.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (token, null);
        }
        catch (DbUpdateException e) when (IsUniqueViolation(e))
        {
            return (null, new(StatusCodes.Status409Conflict, LoginErrors.NameTaken));
        }
    }

    public async Task<(string? Token, LoginError? Error)> SignInAsync(
        string username,
        string password,
        string address,
        bool restore,
        CancellationToken cancellationToken
    )
    {
        var account = await users.FindByNameAsync(username);
        if (account == null || account.PasswordHash == null)
        {
            return (null, new(StatusCodes.Status401Unauthorized, LoginErrors.WrongPassword));
        }

        if (lockout.IsLocked(account.Id, address))
        {
            return (null, new(StatusCodes.Status429TooManyRequests, LoginErrors.LockedOut));
        }

        if (!await users.CheckPasswordAsync(account, password))
        {
            lockout.Failed(account.Id, address);
            return (null, new(StatusCodes.Status401Unauthorized, LoginErrors.WrongPassword));
        }

        lockout.Succeeded(account.Id, address);
        return await StartSessionAsync(account, restore, revokeOthers: false, cancellationToken);
    }

    public async Task<(string? Token, LoginError? Error)> SignInWithCodeAsync(string typed, bool restore, CancellationToken cancellationToken)
    {
        var code = RecoveryCodes.Normalize(typed);
        var hash = code == null ? null : RecoveryCodes.Hash(code);
        var account = hash == null ? null : await db.Accounts.SingleOrDefaultAsync(a => a.RecoveryCodeHash == hash, cancellationToken);
        if (account == null)
        {
            return (null, new(StatusCodes.Status401Unauthorized, LoginErrors.WrongCode));
        }

        return await StartSessionAsync(account, restore, revokeOthers: true, cancellationToken);
    }

    public async Task<(SignedIn? SignedIn, LoginError? Error)> AuthenticateAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var header = http.Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (!header.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            return (null, SignedOut);
        }

        var token = header[bearer.Length..].Trim();
        if (!token.StartsWith(SessionTokens.Prefix, StringComparison.Ordinal))
        {
            return (null, SignedOut);
        }

        var hash = SessionTokens.Hash(token);
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
        var now = Now;
        if (session == null || session.IsExpired(now))
        {
            if (session != null)
            {
                db.Sessions.Remove(session);
                await db.SaveChangesAsync(cancellationToken);
            }

            return (null, SignedOut);
        }

        var account = await db.Accounts.SingleAsync(a => a.Id == session.AccountId, cancellationToken);
        if (account.IsDeleted)
        {
            return (null, SignedOut);
        }

        if (account.IsBanned)
        {
            return (null, new(StatusCodes.Status403Forbidden, LoginErrors.Banned));
        }

        if (now - session.UpdatedAt >= SlideEvery)
        {
            session.ExpiresAt = now + Settings.SessionLifetime;
            account.LastActiveAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return (new(account, session), null);
    }

    public async Task<AccountResponse<object>> DescribeAsync(SignedIn signedIn, CancellationToken cancellationToken)
    {
        var character = await CharacterOfAsync(signedIn.Account.Id, cancellationToken);
        return new()
        {
            Username = signedIn.Account.UserName!,
            Guest = signedIn.Account.IsGuest,
            HasRecoveryCode = signedIn.Account.HasRecoveryCode,
            RecentSignIn = IsRecent(signedIn.Session),
            Character = character == null ? null : rules.Describe(character),
        };
    }

    public async Task<LoginError?> SetPasswordAsync(SignedIn signedIn, string password, CancellationToken cancellationToken)
    {
        var account = signedIn.Account;
        if (!account.IsGuest && !IsRecent(signedIn.Session))
        {
            return new(StatusCodes.Status403Forbidden, LoginErrors.SignInAgain);
        }

        if (password.Length > MaxPasswordLength)
        {
            return new(StatusCodes.Status400BadRequest, LoginErrors.WeakPassword);
        }

        foreach (var validator in users.PasswordValidators)
        {
            if (!(await validator.ValidateAsync(users, account, password)).Succeeded)
            {
                return new(StatusCodes.Status400BadRequest, LoginErrors.WeakPassword);
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        account.PasswordHash = users.PasswordHasher.HashPassword(account, password);
        account.ClaimedAt ??= Now;
        await users.UpdateSecurityStampAsync(account);
        await RevokeOtherSessionsAsync(account.Id, signedIn.Session.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    public async Task<(string? Code, LoginError? Error)> MakeRecoveryCodeAsync(SignedIn signedIn, CancellationToken cancellationToken)
    {
        if (signedIn.Account.IsGuest)
        {
            return (null, new(StatusCodes.Status403Forbidden, LoginErrors.NotClaimed));
        }

        if (!IsRecent(signedIn.Session))
        {
            return (null, new(StatusCodes.Status403Forbidden, LoginErrors.SignInAgain));
        }

        var code = RecoveryCodes.New();
        signedIn.Account.RecoveryCodeHash = RecoveryCodes.Hash(RecoveryCodes.Normalize(code)!);
        signedIn.Account.RecoveryCodeCreatedAt = Now;
        await db.SaveChangesAsync(cancellationToken);
        return (code, null);
    }

    public async Task SignOutAsync(SignedIn signedIn, CancellationToken cancellationToken)
    {
        // A guest's sessions go with it in one transaction, so a failure can't leave it unreachable but still holding its name.
        if (signedIn.Account.IsGuest && await actions.DeleteAsync(signedIn.Account.Id, onlyGuest: true, cancellationToken))
        {
            await actions.PurgeAsync(cancellationToken);
            return;
        }

        db.Sessions.Remove(signedIn.Session);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LoginError?> RenameAsync(SignedIn signedIn, string name, CancellationToken cancellationToken)
    {
        if (rules.CheckName(name) is { } invalid)
        {
            return new(StatusCodes.Status400BadRequest, invalid);
        }

        var account = signedIn.Account;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var renamed = await users.SetUserNameAsync(account, name);
            if (!renamed.Succeeded)
            {
                return NameError(renamed);
            }

            if (rules.CharacterNamesFollowUsername)
            {
                await using var region = await regions.JoinAsync(db, cancellationToken);
                var now = Now;
                await region
                    .Characters.Where(c => c.AccountId == account.Id && c.DeletedAt == null)
                    .ExecuteUpdateAsync(set => set.SetProperty(c => c.Name, name).SetProperty(c => c.UpdatedAt, now), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException e) when (IsUniqueViolation(e))
        {
            return new(StatusCodes.Status409Conflict, LoginErrors.NameTaken);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return new(StatusCodes.Status409Conflict, LoginErrors.NameTaken);
        }
    }

    public async Task<(TicketResponse? Ticket, LoginError? Error)> TicketAsync(SignedIn signedIn, CancellationToken cancellationToken)
    {
        var character = await CharacterOfAsync(signedIn.Account.Id, cancellationToken);
        if (character == null)
        {
            return (null, new(StatusCodes.Status404NotFound, LoginErrors.NoCharacter));
        }

        var server = rules.ServerFor(character);
        return (new() { Ticket = tickets.Issue(signedIn.Account.Id, character.Id, server.ServerId), Server = server.Address }, null);
    }

    private static readonly LoginError SignedOut = new(StatusCodes.Status401Unauthorized, LoginErrors.SignedOut);

    private bool IsRecent(Session session) => Now - session.CreatedAt < Settings.RecentSignIn;

    private async Task<(string? Token, LoginError? Error)> StartSessionAsync(
        Account account,
        bool restore,
        bool revokeOthers,
        CancellationToken cancellationToken
    )
    {
        if (account.IsBanned)
        {
            return (null, new(StatusCodes.Status403Forbidden, LoginErrors.Banned));
        }

        if (account.IsDeleted && !restore)
        {
            return (null, new(StatusCodes.Status409Conflict, LoginErrors.AccountDeleted));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (account.IsDeleted)
        {
            await actions.RestoreAsync(db, account, cancellationToken);
        }

        if (revokeOthers)
        {
            await RevokeOtherSessionsAsync(account.Id, keep: null, cancellationToken);
            await users.UpdateSecurityStampAsync(account);
        }

        account.LastActiveAt = Now;
        var token = await NewSessionAsync(account.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (token, null);
    }

    private async Task<string> NewSessionAsync(long accountId, CancellationToken cancellationToken)
    {
        var token = SessionTokens.New();
        db.Sessions.Add(
            new()
            {
                Id = ids.Next(),
                AccountId = accountId,
                TokenHash = SessionTokens.Hash(token),
                ExpiresAt = Now + Settings.SessionLifetime,
            }
        );
        await db.SaveChangesAsync(cancellationToken);
        return token;
    }

    private Task RevokeOtherSessionsAsync(long accountId, long? keep, CancellationToken cancellationToken) =>
        db.Sessions.Where(s => s.AccountId == accountId && s.Id != keep).ExecuteDeleteAsync(cancellationToken);

    private async Task<TCharacter?> CharacterOfAsync(long accountId, CancellationToken cancellationToken)
    {
        await using var region = regions.Create();
        return await region
            .Characters.AsNoTracking()
            .Where(c => c.AccountId == accountId && c.DeletedAt == null)
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static LoginError NameError(IdentityResult result) =>
        result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName))
            ? new(StatusCodes.Status409Conflict, LoginErrors.NameTaken)
            : new(StatusCodes.Status400BadRequest, LoginErrors.InvalidName);

    internal static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
