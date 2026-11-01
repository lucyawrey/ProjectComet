using Microsoft.EntityFrameworkCore;

namespace Comet.Data.Accounts;

/// <summary>
/// The account database (the <c>account</c> schema): accounts and sessions. Only the Login server uses it. A game
/// subclasses it to own the migrations; Identity's side tables (claims, external logins, tokens, passkeys) aren't
/// in the model.
/// </summary>
public abstract class CometAccountContext(DbContextOptions options) : DbContext(options)
{
    public const string Schema = "account";

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Session> Sessions => Set<Session>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Account>(account =>
        {
            account.ToTable("account");
            account.HasKey(a => a.Id);
            account.Property(a => a.Id).ValueGeneratedNever();
            account.Property(a => a.UserName).HasColumnName("username").IsRequired();
            account.Property(a => a.NormalizedUserName).HasColumnName("normalized_username").IsRequired();
            account.HasIndex(a => a.NormalizedUserName).IsUnique();
            account.Property(a => a.ConcurrencyStamp).IsConcurrencyToken();
            account.HasIndex(a => a.RecoveryCodeHash).IsUnique().HasFilter("recovery_code_hash IS NOT NULL");
            account.ToTable(t =>
            {
                t.HasCheckConstraint("ck_account_kind", "kind IN (1, 2)");
                t.HasCheckConstraint("ck_account_status", "status IN (1, 2)");
            });

            account.Ignore(a => a.Email);
            account.Ignore(a => a.NormalizedEmail);
            account.Ignore(a => a.EmailConfirmed);
            account.Ignore(a => a.PhoneNumber);
            account.Ignore(a => a.PhoneNumberConfirmed);
            account.Ignore(a => a.TwoFactorEnabled);
            account.Ignore(a => a.IsDeleted);
            account.Ignore(a => a.IsGuest);
            account.Ignore(a => a.IsBanned);
            account.Ignore(a => a.HasRecoveryCode);
        });

        modelBuilder.Entity<Session>(session =>
        {
            session.ToTable("session");
            session.Property(s => s.Id).ValueGeneratedNever();
            session.HasIndex(s => s.TokenHash).IsUnique();
            session.HasOne<Account>().WithMany().HasForeignKey(s => s.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
