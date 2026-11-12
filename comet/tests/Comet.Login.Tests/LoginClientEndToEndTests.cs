using Comet.Client.Login;
using Comet.Protocol.Login;

namespace Comet.Login.Tests;

public sealed class LoginClientEndToEndTests(PostgresContainer postgres)
{
    private sealed class Tokens : ITokenStore
    {
        public string? Saved { get; set; }

        public string? Load() => Saved;

        public void Save(string? token) => Saved = token;
    }

    private sealed class NewCharacter
    {
        public int Look { get; set; }
    }

    [Fact]
    public async Task AGuestPlaysClaimsAndSignsInElsewhere()
    {
        await using var host = await LoginHost.StartAsync(postgres);
        var laptop = new LoginClient<TestView>(new HttpLoginTransport(host.NewClient().Http), new Tokens());
        var guest = new LoginClient<NewCharacter>(new HttpLoginTransport(host.NewClient().Http), new Tokens());

        Assert.True((await guest.CreateGuestAsync("Bob", new NewCharacter { Look = 2 })).Succeeded);
        Assert.Equal(LoginErrors.NameTaken, (await guest.CreateGuestAsync("bob", new NewCharacter { Look = 2 })).Error);
        Assert.Equal(LoginErrors.WrongPassword, (await laptop.SignInAsync("Bob", "nothing")).Error);
        var me = await new LoginClient<TestView>(new HttpLoginTransport(host.NewClient().Http), new Tokens { Saved = guest.Token }).GetAccountAsync();
        Assert.Equal(("Bob", 2, true), (me.Value!.Character!.Name, me.Value.Character.Look, me.Value.Guest));

        Assert.True((await guest.SetPasswordAsync("a long password")).Succeeded);
        var ticket = await guest.GetTicketAsync();
        Assert.Equal(TestRules.ServerAddress, ticket.Value!.Server);

        Assert.True((await laptop.SignInAsync("bob", "a long password")).Succeeded);
        var code = await laptop.MakeRecoveryCodeAsync();
        Assert.True(code.Succeeded);
        await laptop.SignOutAsync();
        Assert.Equal(LoginErrors.SignedOut, (await laptop.GetAccountAsync()).Error);
        Assert.True((await laptop.SignInWithCodeAsync(code.Value!.Code)).Succeeded);
    }
}
