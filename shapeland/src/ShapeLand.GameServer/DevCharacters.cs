#if DEBUG
using Comet.Data.Characters;
using ShapeLand.Data;

namespace ShapeLand.GameServer;

/// <summary>
/// The characters of a development server without accounts (<see cref="ShapeLandServerOptions.DevJoins"/>): picked
/// by name, people's on one account and bots' on another, so the joins' account check holds as with tickets.
/// </summary>
public sealed class DevCharacters(InMemoryCharacterDatabase<ShapeLandCharacter> database)
{
    public const long AccountId = 1;

    /// <summary>The bots' account, so they're kept apart from people's characters (the editor comes back as a person's).</summary>
    public const long BotAccountId = 2;

    public InMemoryCharacterDatabase<ShapeLandCharacter> Database => database;

    /// <summary>
    /// The character with this name, ignoring case, or a new one with this look, on the people's or the bots'
    /// account; null if the name is the other account's.
    /// </summary>
    public long? FindOrAdd(string name, bool bot, int shape, int color, int eyeColor)
    {
        var account = bot ? BotAccountId : AccountId;
        var (id, _) = database.FindOrAdd(
            name,
            id => new ShapeLandCharacter
            {
                Id = id,
                AccountId = account,
                Name = name,
                Shape = shape,
                Color = color,
                EyeColor = eyeColor,
            }
        );
        return database.Character(id)?.AccountId == account ? id : null;
    }

    /// <summary>The account a character is on.</summary>
    public long AccountOf(long characterId) => database.Character(characterId)?.AccountId ?? AccountId;
}
#endif
