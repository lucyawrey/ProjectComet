using System.Text.Json;
using Comet.Login;
using Comet.Protocol.Login;
using ShapeLand.Data;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;

namespace ShapeLand.LoginServer;

public sealed class ShapeLandLoginRules(ShapeLandContent content, GameServerAddress gameServer) : ILoginRules<ShapeLandCharacter>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool CharacterNamesFollowUsername => true;
    public bool CharactersDeletedWithAccount => true;

    public string? CheckName(string name) =>
        ShapeLandRules.TryNormalizeName(name, out var normalized) && normalized == name ? null : LoginErrors.InvalidName;

    public ShapeLandCharacter? CreateCharacter(long characterId, long accountId, string username, JsonElement request)
    {
        NewCharacter? look;
        try
        {
            look = request.ValueKind == JsonValueKind.Object ? request.Deserialize<NewCharacter>(Json) : null;
        }
        catch (JsonException)
        {
            return null;
        }

        if (
            look == null
            || !content.Shapes.TryGet(look.Shape, out _)
            || !content.Colors.TryGet(look.Color, out _)
            || !content.Colors.TryGet(look.EyeColor, out _)
        )
        {
            return null;
        }

        return new ShapeLandCharacter
        {
            Id = characterId,
            AccountId = accountId,
            Name = username,
            Shape = look.Shape,
            Color = look.Color,
            EyeColor = look.EyeColor,
        };
    }

    public object Describe(ShapeLandCharacter character) =>
        new CharacterView
        {
            Name = character.Name,
            Shape = character.Shape,
            Color = character.Color,
            EyeColor = character.EyeColor,
        };

    public GameServerAddress ServerFor(ShapeLandCharacter character) => gameServer;
}
