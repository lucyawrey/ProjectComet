using Microsoft.EntityFrameworkCore;

namespace Comet.Data.Characters;

/// <summary>
/// The <c>region</c> schema, shared by the Login server and the game servers. A game subclasses it with its own
/// <typeparamref name="TCharacter"/> and owns the migrations.
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

        // Comet's Character is an unmapped base: no hierarchy or discriminator, and the game's columns are NOT NULL.
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

public static class CometRegion
{
    public const string Schema = "region";

    public const string CharacterTable = "character";
    public const string CharacterStatusTable = "character_status";
    public const string CharacterOwnerTable = "character_owner";

    public const string CaseInsensitive = "case_insensitive";
}
