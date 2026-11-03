#if DEBUG
using Comet.Data.Characters;
using ShapeLand.Data;

namespace ShapeLand.GameServer;

/// <summary>
/// The characters of a development server without accounts (<see cref="ShapeLandServerOptions.DevJoins"/>): picked
/// by name, all on one account, so the joins' account check holds as with tickets.
/// </summary>
public sealed class DevCharacters(InMemoryCharacterDatabase<ShapeLandCharacter> database)
{
    public const long AccountId = 1;

    public InMemoryCharacterDatabase<ShapeLandCharacter> Database => database;

    /// <summary>The character with this name, ignoring case, or a new one with this look.</summary>
    public long FindOrAdd(string name, int shape, int color, int eyeColor) =>
        database.FindOrAdd(name, id => new ShapeLandCharacter
        {
            Id = id, AccountId = AccountId, Name = name, Shape = shape, Color = color, EyeColor = eyeColor,
        }).Id;
}
#endif
