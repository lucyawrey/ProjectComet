using Comet.Client.Login;
using Comet.Protocol.Login;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;

namespace ShapeLand.Bots;

/// <summary>A join ticket for a bot, the game server it's for, and the name the bot plays under.</summary>
public sealed record BotTicket(string Ticket, Uri Server, string Name);

/// <summary>Where a bot gets its join ticket: a real account on the Login server, or (in tests) a stand-in.</summary>
public interface IBotAccount
{
    /// <summary>Signs in (making the account if need be) and gets a join ticket, or null with the reason in <paramref name="failure"/>.</summary>
    Task<(BotTicket? Ticket, string? Failure)> TicketAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A bot's guest account on the Login server, like a player's: made on the first run with a random look, its token
/// kept in a file so later runs reuse it, and made again if the token is refused. If an older account holds the
/// name, a random 4-character suffix is added.
/// </summary>
public sealed class LoginBotAccount(string name, Uri loginServer, string tokenFolder, ShapeLandContent content, int seed) : IBotAccount
{
    private const int SuffixLength = 4;
    private const int Attempts = 5;
    private const string SuffixCharacters = "abcdefghijkmnpqrstuvwxyz23456789";

    private readonly Random _random = new(seed);

    public async Task<(BotTicket? Ticket, string? Failure)> TicketAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient { BaseAddress = loginServer, Timeout = TimeSpan.FromSeconds(15) };
        var tokens = new FileTokenStore(Path.Combine(tokenFolder, $"{name}.token"));
        var login = new LoginClient<CharacterView>(new HttpLoginTransport(http), tokens);

        if (login.SignedIn && await TryTicketAsync(login) is { } reused)
        {
            return (reused, null);
        }

        // No account yet, or its token was refused: a new guest, with a suffix if an older account holds the name.
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

            tryName = name + new string(Enumerable.Range(0, SuffixLength).Select(_ => SuffixCharacters[_random.Next(SuffixCharacters.Length)]).ToArray());
        }

        return (null, LoginErrors.NameTaken);
    }

    private async Task<BotTicket?> TryTicketAsync(LoginClient<CharacterView> login)
    {
        var account = await login.GetAccountAsync();
        var ticket = account.Succeeded ? await login.GetTicketAsync() : default;
        return ticket.Succeeded ? new BotTicket(ticket.Value!.Ticket, new Uri(ticket.Value.Server), account.Value!.Username) : null;
    }

    private NewCharacter RandomLook()
    {
        var colors = content.Colors.All;
        return new NewCharacter
        {
            Shape = content.Shapes.All[_random.Next(content.Shapes.All.Count)].Number,
            Color = colors[_random.Next(colors.Count)].Number,
            EyeColor = colors[_random.Next(colors.Count)].Number,
        };
    }

    // One file per bot. A bot's token only plays that bot, which a public test build gives away anyway.
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
