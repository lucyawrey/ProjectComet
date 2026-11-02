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
            character.ToTable(CometRegion.CharacterTable);
            character.Property(c => c.Id).ValueGeneratedNever();
            character.HasIndex(c => c.AccountId);
            character.Property(c => c.Name).UseCollation(CometRegion.CaseInsensitive);
            character.HasIndex(c => c.Name).IsUnique().HasFilter("deleted_at IS NULL"); // only live characters hold names
        });

        modelBuilder.Entity<CharacterStatus>(status =>
        {
            status.ToTable(CometRegion.CharacterStatusTable);
            status.HasKey(s => s.CharacterId);
            status.HasOne<TCharacter>().WithOne().HasForeignKey<CharacterStatus>(s => s.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterOwner>(owner =>
        {
            owner.ToTable(CometRegion.CharacterOwnerTable);
            owner.HasKey(o => o.CharacterId);
            owner.HasOne<TCharacter>().WithOne().HasForeignKey<CharacterOwner>(o => o.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>Names shared by every game's region context.</summary>
public static class CometRegion
{
    public const string Schema = "region";

    public const string CharacterTable = "character";
    public const string CharacterStatusTable = "character_status";
    public const string CharacterOwnerTable = "character_owner";

    /// <summary>A case-insensitive ICU collation, for names unique ignoring case.</summary>
    public const string CaseInsensitive = "case_insensitive";
}
