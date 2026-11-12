using System.Text.Json;
using Comet.Data.Characters;

namespace Comet.Login;

public interface ILoginRules<TCharacter>
    where TCharacter : Character
{
    /// <summary>Checks a username, returning an error code (<c>LoginErrors</c>) or null if it's allowed.</summary>
    string? CheckName(string name);

    TCharacter? CreateCharacter(long characterId, long accountId, string username, JsonElement request);

    /// <summary>Serialized as JSON for the client.</summary>
    object Describe(TCharacter character);

    GameServerAddress ServerFor(TCharacter character);

    bool CharacterNamesFollowUsername { get; }

    bool CharactersDeletedWithAccount { get; }
}

/// <summary><c>ServerId</c> is the node number that tickets name.</summary>
public sealed record GameServerAddress(int ServerId, string Address);
