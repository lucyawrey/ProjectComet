#if DEBUG
using Comet.Data.Characters;
using ShapeLand.Data;

namespace ShapeLand.GameServer;

/// <summary>
/// The characters of a server without accounts, picked by name. People's and bots' are on separate accounts, so the
/// joins' account check holds as with tickets.
/// </summary>
public sealed class DevCharacters(InMemoryCharacterDatabase<ShapeLandCharacter> database)
{
    public const long AccountId = 1;

    public const long BotAccountId = 2;

    public InMemoryCharacterDatabase<ShapeLandCharacter> Database => database;

    /// <summary>Null if the name belongs to the other account.</summary>
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

    public long AccountOf(long characterId) => database.Character(characterId)?.AccountId ?? AccountId;
}
#endif
