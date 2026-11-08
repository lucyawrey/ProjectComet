using Comet.Data.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Comet.Login;

/// <summary>
/// Only usernames, password hashes and security stamps. Any other Identity feature fails clearly, since its tables
/// don't exist.
/// </summary>
public sealed class CometUserStore(CometAccountContext db) : IUserPasswordStore<Account>, IUserSecurityStampStore<Account>
{
    public async Task<IdentityResult> CreateAsync(Account user, CancellationToken cancellationToken)
    {
        db.Accounts.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(Account user, CancellationToken cancellationToken)
    {
        if (db.Entry(user).State == EntityState.Detached)
        {
            db.Accounts.Attach(user).State = EntityState.Modified;
        }

        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
        }

        return IdentityResult.Success;
    }

    public Task<IdentityResult> DeleteAsync(Account user, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Accounts are deleted by setting deleted_at (AccountActions), never through Identity.");

    public Task<Account?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        long.TryParse(userId, out var id) ? db.Accounts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken) : Task.FromResult<Account?>(null);

    public Task<Account?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        db.Accounts.SingleOrDefaultAsync(a => a.NormalizedUserName == normalizedUserName, cancellationToken);

    public Task<string> GetUserIdAsync(Account user, CancellationToken cancellationToken) => Task.FromResult(user.Id.ToString());

    public Task<string?> GetUserNameAsync(Account user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);

    public Task<string?> GetNormalizedUserNameAsync(Account user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);

    public Task SetUserNameAsync(Account user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task SetNormalizedUserNameAsync(Account user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public Task SetPasswordHashAsync(Account user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(Account user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(Account user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash != null);

    public Task SetSecurityStampAsync(Account user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(Account user, CancellationToken cancellationToken) => Task.FromResult(user.SecurityStamp);

    public void Dispose() { }
}
