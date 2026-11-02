using Comet.Client.Login;
using Comet.Protocol.Login;

namespace Comet.Client.Tests;

public class LoginClientTests
{
    private sealed class Tokens : ITokenStore
    {
        public string? Saved { get; set; }
        public string? Load() => Saved;
        public void Save(string? token) => Saved = token;
    }

    private sealed class Transport(params LoginHttpResponse[] responses) : ITokenStore, ILoginTransport
    {
        private readonly Queue<LoginHttpResponse> _responses = new(responses);
        public List<(string Method, string Path, string? Json, string? Token)> Sent { get; } = [];

        public Task<LoginHttpResponse> SendAsync(string method, string path, string? json, string? bearerToken)
        {
            Sent.Add((method, path, json, bearerToken));
            return Task.FromResult(_responses.Dequeue());
        }

        public string? Load() => null;
        public void Save(string? token) { }
    }

    private sealed class Look
    {
        public int Shape { get; set; }
    }

    private static LoginHttpResponse Problem(int status, string code) => new(status, $$"""{"status":{{status}},"code":"{{code}}"}""");

    [Fact]
    public async Task ANewGuestKeepsItsToken()
    {
        var tokens = new Tokens();
        var transport = new Transport(new LoginHttpResponse(200, """{"token":"cs_abc"}"""));
        var client = new LoginClient<Look>(transport, tokens);

        var result = await client.CreateGuestAsync("Alice", new Look { Shape = 2 });

        Assert.True(result.Succeeded);
        Assert.Equal("cs_abc", tokens.Saved);
        Assert.Equal(("POST", LoginPaths.Guest, """{"name":"Alice","character":{"shape":2}}""", (string?)null), transport.Sent.Single());
    }

    [Fact]
    public async Task SignedInCallsSendTheTokenAndReadTheGamesCharacter()
    {
        var tokens = new Tokens { Saved = "cs_abc" };
        var transport = new Transport(new LoginHttpResponse(200, """{"username":"Alice","guest":true,"character":{"shape":4}}"""));
        var client = new LoginClient<Look>(transport, tokens);

        var me = await client.GetAccountAsync();

        Assert.Equal("cs_abc", transport.Sent.Single().Token);
        Assert.Equal("Alice", me.Value!.Username);
        Assert.Equal(4, me.Value.Character!.Shape);
    }

    [Fact]
    public async Task SigningInAgainRevokesTheOldSession()
    {
        var tokens = new Tokens { Saved = "cs_old" };
        var transport = new Transport(new LoginHttpResponse(200, """{"token":"cs_new"}"""), new LoginHttpResponse(204, ""));
        var client = new LoginClient<Look>(transport, tokens);

        Assert.True((await client.SignInAgainAsync("Alice", "correct horse")).Succeeded);

        Assert.Equal("cs_new", tokens.Saved);
        Assert.Equal(("POST", LoginPaths.SignOut, (string?)null, (string?)"cs_old"), transport.Sent[1]);
    }

    [Fact]
    public async Task ARefusedTokenIsForgotten()
    {
        var tokens = new Tokens { Saved = "cs_old" };
        var client = new LoginClient<Look>(new Transport(Problem(401, LoginErrors.SignedOut)), tokens);

        var me = await client.GetAccountAsync();

        Assert.Equal(LoginErrors.SignedOut, me.Error);
        Assert.Null(tokens.Saved);
        Assert.False(client.SignedIn);
    }

    [Fact]
    public async Task OtherErrorsKeepTheToken()
    {
        var tokens = new Tokens { Saved = "cs_abc" };
        var client = new LoginClient<Look>(new Transport(Problem(409, LoginErrors.NameTaken)), tokens);

        Assert.Equal(LoginErrors.NameTaken, (await client.RenameAsync("Bob")).Error);
        Assert.Equal("cs_abc", tokens.Saved);
    }

    [Fact]
    public async Task NoAnswerIsUnreachable()
    {
        var tokens = new Tokens { Saved = "cs_abc" };
        var client = new LoginClient<Look>(new Transport(new LoginHttpResponse(0, "refused"), new LoginHttpResponse(502, "<html>Bad gateway</html>")), tokens);

        Assert.Equal(LoginClient<Look>.Unreachable, (await client.GetTicketAsync()).Error);
        Assert.Equal(LoginClient<Look>.Unreachable, (await client.GetTicketAsync()).Error);
        Assert.Equal("cs_abc", tokens.Saved);
    }

    [Fact]
    public async Task SigningOutForgetsTheTokenEvenWhenTheServerCantBeReached()
    {
        var tokens = new Tokens { Saved = "cs_abc" };
        var client = new LoginClient<Look>(new Transport(new LoginHttpResponse(0, "refused")), tokens);

        await client.SignOutAsync();

        Assert.Null(tokens.Saved);
    }
}
