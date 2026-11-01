using Microsoft.EntityFrameworkCore;

namespace Comet.Data.Characters;

/// <summary>
/// The region database (the <c>region</c> schema): characters, their status and ownership. The Login server and the
/// game servers use it. A game subclasses it, adds its own columns with
/// <see cref="CharacterColumns.AddCharacterColumns{T}"/>, and owns the migrations.
/// </summary>
public abstract class CometRegionContext(DbContextOptions options) : DbContext(options)
{
    public const string Schema = "region";

    /// <summary>A case-insensitive ICU collation, for names unique ignoring case.</summary>
    public const string CaseInsensitive = "case_insensitive";

    public DbSet<Character> Characters => Set<Character>();
    public DbSet<CharacterStatus> CharacterStatuses => Set<CharacterStatus>();
    public DbSet<CharacterOwner> CharacterOwners => Set<CharacterOwner>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        // In public, which every connection searches: Npgsql writes column collations without a schema.
        modelBuilder.HasCollation("public", CaseInsensitive, locale: "und-u-ks-level2", provider: "icu", deterministic: false);

        modelBuilder.Entity<Character>(character =>
        {
            character.ToTable("character");
            character.Property(c => c.Id).ValueGeneratedNever();
            character.HasIndex(c => c.AccountId);
            character.Property(c => c.Name).UseCollation(CaseInsensitive);
            character.HasIndex(c => c.Name).IsUnique();
        });

        modelBuilder.Entity<CharacterStatus>(status =>
        {
            status.ToTable("character_status");
            status.HasKey(s => s.CharacterId);
            status.HasOne<Character>().WithOne().HasForeignKey<CharacterStatus>(s => s.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterOwner>(owner =>
        {
            owner.ToTable("character_owner");
            owner.HasKey(o => o.CharacterId);
            owner.HasOne<Character>().WithOne().HasForeignKey<CharacterOwner>(o => o.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
