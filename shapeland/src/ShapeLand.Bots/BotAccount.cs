using Comet.Client.Login;
using Comet.Protocol.Login;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;

namespace ShapeLand.Bots;

public sealed record BotJoin(string? Ticket, Uri Server, string Name, NewCharacter? Look = null);

public interface IBotAccount
{
    Task<(BotJoin? Join, string? Failure)> JoinAsync(CancellationToken cancellationToken);
}

public sealed class DevBotAccount(string name, Uri server, ShapeLandContent content, int seed) : IBotAccount
{
    public Task<(BotJoin? Join, string? Failure)> JoinAsync(CancellationToken cancellationToken) =>
        Task.FromResult<(BotJoin?, string?)>((new BotJoin(null, server, name, BotLooks.Random(content, new Random(seed))), null));
}

internal static class BotLooks
{
    public static NewCharacter Random(ShapeLandContent content, Random random)
    {
        var colors = content.Colors.All;
        return new NewCharacter
        {
            Shape = content.Shapes.All[random.Next(content.Shapes.All.Count)].Number,
            Color = colors[random.Next(colors.Count)].Number,
            EyeColor = colors[random.Next(colors.Count)].Number,
        };
    }
}

public sealed class LoginBotAccount(string name, Uri loginServer, string tokenFolder, ShapeLandContent content, int seed, Uri? serverOverride = null)
    : IBotAccount
{
    private const int SuffixLength = 4;
    private const int Attempts = 5;
    private const string SuffixCharacters = "abcdefghijkmnpqrstuvwxyz23456789";

    private readonly Random _random = new(seed);

    public async Task<(BotJoin? Join, string? Failure)> JoinAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient { BaseAddress = loginServer, Timeout = TimeSpan.FromSeconds(15) };
        var tokens = new FileTokenStore(Path.Combine(tokenFolder, $"{name}.token"));
        var login = new LoginClient<CharacterView>(new HttpLoginTransport(http), tokens);

        if (login.SignedIn && await TryTicketAsync(login) is { } reused)
        {
            return (reused, null);
        }

        var tryName = name;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var created = await login.CreateGuestAsync(tryName, RandomLook());
            if (created.Succeeded)
            {
                return await TryTicketAsync(login) is { } ticket ? (ticket, null) : (null, "no ticket for the new account");
            }

            if (created.Error != LoginErrors.NameTaken)
            {
                return (null, created.Error);
            }

            tryName =
                name + new string(Enumerable.Range(0, SuffixLength).Select(_ => SuffixCharacters[_random.Next(SuffixCharacters.Length)]).ToArray());
        }

        return (null, LoginErrors.NameTaken);
    }

    private async Task<BotJoin?> TryTicketAsync(LoginClient<CharacterView> login)
    {
        var account = await login.GetAccountAsync();
        if (!account.Succeeded || account.Value is not { } signedIn)
        {
            return null;
        }

        var ticket = await login.GetTicketAsync();
        return ticket.Succeeded && ticket.Value is { } got ? new BotJoin(got.Ticket, serverOverride ?? new Uri(got.Server), signedIn.Username) : null;
    }

    private NewCharacter RandomLook() => BotLooks.Random(content, _random);

    private sealed class FileTokenStore(string path) : ITokenStore
    {
        public string? Load() => File.Exists(path) ? File.ReadAllText(path).Trim() : null;

        public void Save(string? token)
        {
            if (token == null)
            {
                File.Delete(path);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, token);
        }
    }
}
