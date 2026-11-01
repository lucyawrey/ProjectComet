using Microsoft.EntityFrameworkCore;

namespace Comet.Data.Characters;

/// <summary>
/// The region database (the <c>region</c> schema): characters, their status and ownership. The Login server and the
/// game servers use it. A game subclasses it with its own character class, <typeparamref name="TCharacter"/> (which
/// adds the game's columns to the <c>character</c> table), and owns the migrations.
/// </summary>
public abstract class CometRegionContext<TCharacter>(DbContextOptions options) : DbContext(options)
    where TCharacter : Character
{
    public DbSet<TCharacter> Characters => Set<TCharacter>();
    public DbSet<CharacterStatus> CharacterStatuses => Set<CharacterStatus>();
    public DbSet<CharacterOwner> CharacterOwners => Set<CharacterOwner>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(CometRegion.Schema);
        // In public, which every connection searches: Npgsql writes column collations without a schema.
        modelBuilder.HasCollation("public", CometRegion.CaseInsensitive, locale: "und-u-ks-level2", provider: "icu", deterministic: false);

        // Only the game's class is an entity: Comet's Character is its unmapped base, so there's no hierarchy, no
        // discriminator, and the game's columns are NOT NULL where their types are.
        modelBuilder.Ignore<Character>();
        modelBuilder.Entity<TCharacter>(character =>
        {
            character.ToTable("character");
            character.Property(c => c.Id).ValueGeneratedNever();
            character.Ignore(c => c.IsDeleted);
            character.HasIndex(c => c.AccountId);
            character.Property(c => c.Name).UseCollation(CometRegion.CaseInsensitive);
            character.HasIndex(c => c.Name).IsUnique().HasFilter("deleted_at IS NULL"); // only live characters hold names
        });

        modelBuilder.Entity<CharacterStatus>(status =>
        {
            status.ToTable("character_status");
            status.HasKey(s => s.CharacterId);
            status.HasOne<TCharacter>().WithOne().HasForeignKey<CharacterStatus>(s => s.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterOwner>(owner =>
        {
            owner.ToTable("character_owner");
            owner.HasKey(o => o.CharacterId);
            owner.Ignore(o => o.IsHeld);
            owner.HasOne<TCharacter>().WithOne().HasForeignKey<CharacterOwner>(o => o.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>Names shared by every game's region context.</summary>
public static class CometRegion
{
    public const string Schema = "region";

    /// <summary>A case-insensitive ICU collation, for names unique ignoring case.</summary>
    public const string CaseInsensitive = "case_insensitive";
}
