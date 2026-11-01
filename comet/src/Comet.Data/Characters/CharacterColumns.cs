using Microsoft.EntityFrameworkCore;

namespace Comet.Data.Characters;

/// <summary>A game's own columns on the <c>character</c> table, keyed by the character's ID.</summary>
public interface ICharacterColumns
{
    long Id { get; set; }
}

/// <summary>Adds a game's columns to Comet's tables (EF Core table splitting).</summary>
public static class CharacterColumns
{
    /// <summary>
    /// Maps <typeparamref name="T"/> onto the <c>character</c> table, sharing its key. Every character has a row of
    /// <typeparamref name="T"/> (a required dependent), so its columns are <c>NOT NULL</c> where their types are, and
    /// a character must be added together with its <typeparamref name="T"/>.
    /// </summary>
    public static void AddCharacterColumns<T>(this ModelBuilder modelBuilder) where T : class, ICharacterColumns
    {
        var entity = modelBuilder.Entity<T>();
        entity.ToTable("character");
        entity.Property(c => c.Id).ValueGeneratedNever();
        var key = entity.HasOne<Character>().WithOne().HasForeignKey<T>(c => c.Id);
        key.Metadata.IsRequiredDependent = true;
    }
}
