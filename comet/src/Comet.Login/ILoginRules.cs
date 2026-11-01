using System.Text.Json;
using Comet.Data.Characters;

namespace Comet.Login;

/// <summary>The game's rules for the Login server: what Comet leaves to each game.</summary>
public interface ILoginRules<TCharacter>
    where TCharacter : Character
{
    /// <summary>Checks a username, returning an error code (<c>LoginErrors</c>) or null if it's allowed.</summary>
    string? CheckName(string name);

    /// <summary>
    /// Makes a new guest's character from the game's part of the request, or returns null if the request isn't
    /// allowed. The character's name is the game's choice.
    /// </summary>
    TCharacter? CreateCharacter(long characterId, long accountId, string username, JsonElement request);

    /// <summary>What the client gets about its character (serialized as JSON).</summary>
    object Describe(TCharacter character);

    /// <summary>The game server a character plays on.</summary>
    GameServerAddress ServerFor(TCharacter character);

    /// <summary>Renaming the account renames its live characters too.</summary>
    bool CharacterNamesFollowUsername { get; }

    /// <summary>Deleting the account deletes its characters too, and restoring it restores them.</summary>
    bool CharactersDeletedWithAccount { get; }
}

/// <summary>A game server: its node number (tickets name it) and the WebSocket address clients connect to.</summary>
public sealed record GameServerAddress(int ServerId, string Address);
