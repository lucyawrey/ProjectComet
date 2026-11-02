using System.Net;
using System.Net.Http.Json;
using Comet.Data.Accounts;
using Comet.Protocol.Login;
using Comet.Tickets;
using Microsoft.EntityFrameworkCore;

namespace Comet.Login.Tests;

public sealed class LoginTests(PostgresContainer postgres) : IAsyncLifetime
{
    private LoginHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await LoginHost.StartAsync(postgres);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task AGuestGetsAnAccountAndCharacter()
    {
        var guest = await _host.GuestAsync("Alice", look: 3);
        var me = await guest.MeAsync();

        Assert.Equal("Alice", me.Username);
        Assert.True(me.Guest);
        Assert.False(me.HasRecoveryCode);
        Assert.Equal("Alice", me.Character!.Name);
        Assert.Equal(3, me.Character.Look);
        Assert.StartsWith("cs_", guest.Token);
    }

    [Fact]
    public async Task BadNamesAndCharactersAreRefused()
    {
        var client = _host.NewClient();
        Assert.Equal(LoginErrors.InvalidName, await client.ErrorOfAsync(client.PostAsync(LoginPaths.Guest, new { name = "a b", character = new { look = 1 } })));
        Assert.Equal(LoginErrors.InvalidName, await client.ErrorOfAsync(client.PostAsync(LoginPaths.Guest, new { name = "ab", character = new { look = 1 } })));
        Assert.Equal(LoginErrors.InvalidCharacter, await client.ErrorOfAsync(client.PostAsync(LoginPaths.Guest, new { name = "Okay", character = new { look = 99 } })));

        await using var region = _host.Region();
        Assert.Empty(await region.Characters.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NamesAreUniqueIgnoringCase()
    {
        await _host.GuestAsync("Alice");
        var client = _host.NewClient();
        Assert.Equal(LoginErrors.NameTaken, await client.ErrorOfAsync(client.PostAsync(LoginPaths.Guest, new { name = "ALICE", character = new { look = 1 } })));
    }

    [Fact]
    public async Task TwoGuestsTakingOneNameAtOnceMakeOneAccount()
    {
        var attempts = Enumerable.Range(0, 8).Select(_ =>
        {
            var client = _host.NewClient();
            return client.PostAsync(LoginPaths.Guest, new { name = "Racer", character = new { look = 1 } });
        });
        var responses = await Task.WhenAll(attempts);

        Assert.Single(responses, r => r.IsSuccessStatusCode);
        foreach (var refused in responses.Where(r => !r.IsSuccessStatusCode))
        {
            Assert.Equal(LoginErrors.NameTaken, await LoginHost.Client.ErrorAsync(refused));
        }

        await using var accounts = _host.Accounts();
        await using var region = _host.Region();
        Assert.Equal(1, await accounts.Accounts.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await region.Characters.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RenamingRenamesTheAccountAndCharacterAndFreesTheOldName()
    {
        var alice = await _host.GuestAsync("Alice");
        await _host.GuestAsync("Bob");

        Assert.Equal(LoginErrors.NameTaken, await alice.ErrorOfAsync(alice.PostAsync(LoginPaths.Rename, new { name = "bob" })));
        Assert.Null(await alice.ErrorOfAsync(alice.PostAsync(LoginPaths.Rename, new { name = "Alicia" })));
        Assert.Null(await alice.ErrorOfAsync(alice.PostAsync(LoginPaths.Rename, new { name = "ALICIA" }))); // only the capitals

        var me = await alice.MeAsync();
        Assert.Equal("ALICIA", me.Username);
        Assert.Equal("ALICIA", me.Character!.Name);
        await _host.GuestAsync("Alice"); // free at once
    }

    [Fact]
    public async Task SigningOutRevokesTheToken()
    {
        var guest = await _host.GuestAsync("Alice");
        Assert.Null(await guest.ErrorOfAsync(guest.PostAsync(LoginPaths.SignOut)));
        Assert.Equal(LoginErrors.SignedOut, await guest.ErrorOfAsync(guest.GetAsync(LoginPaths.Me)));
        await _host.GuestAsync("Alice"); // a guest can't come back, so its name is free at once

        var alicia = await ClaimedAsync("Alicia", "a password");
        Assert.Null(await alicia.ErrorOfAsync(alicia.PostAsync(LoginPaths.SignOut)));
        Assert.Null(await _host.NewClient().SignInAsync("Alicia", "a password")); // a claimed account stays
    }

    [Fact]
    public async Task UnknownAndMissingTokensAreSignedOut()
    {
        var client = _host.NewClient();
        Assert.Equal(LoginErrors.SignedOut, await client.ErrorOfAsync(client.GetAsync(LoginPaths.Me)));
        client.Token = SessionTokens.New();
        Assert.Equal(LoginErrors.SignedOut, await client.ErrorOfAsync(client.GetAsync(LoginPaths.Me)));
    }

    [Fact]
    public async Task ClaimingSetsAPasswordThatSignsInElsewhere()
    {
        var guest = await _host.GuestAsync("Alice");
        _host.Clock.Advance(TimeSpan.FromDays(1)); // a guest's first password needs no recent sign-in
        Assert.Null(await guest.ErrorOfAsync(guest.PostAsync(LoginPaths.Password, new { password = "correct horse" })));
        Assert.False((await guest.MeAsync()).Guest);

        var phone = _host.NewClient();
        Assert.Null(await phone.SignInAsync("alice", "correct horse"));
        Assert.Equal("Alice", (await phone.MeAsync()).Username);
        Assert.Equal(LoginErrors.WrongPassword, await phone.SignInAsync("Alice", "wrong horse"));
        Assert.Equal(LoginErrors.WrongPassword, await phone.SignInAsync("Nobody", "correct horse"));
    }

    [Fact]
    public async Task GuestsCantSignInWithAPassword()
    {
        await _host.GuestAsync("Alice");
        var other = _host.NewClient();
        Assert.Equal(LoginErrors.WrongPassword, await other.SignInAsync("Alice", ""));
    }

    [Fact]
    public async Task ANullFieldIsABadRequest()
    {
        var response = await _host.NewClient().PostAsync(LoginPaths.SignIn, new { username = (string?)null, password = "correct horse" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShortPasswordsAreRefused()
    {
        var guest = await _host.GuestAsync("Alice");
        Assert.Equal(LoginErrors.WeakPassword, await guest.ErrorOfAsync(guest.PostAsync(LoginPaths.Password, new { password = "1234567" })));
        Assert.Null(await guest.ErrorOfAsync(guest.PostAsync(LoginPaths.Password, new { password = "12345678" })));
    }

    [Fact]
    public async Task ChangingThePasswordNeedsARecentSignInAndRevokesOtherSessions()
    {
        var laptop = await ClaimedAsync("Alice", "first password");
        var phone = _host.NewClient();
        Assert.Null(await phone.SignInAsync("Alice", "first password"));

        _host.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.False((await phone.MeAsync()).RecentSignIn);
        Assert.Equal(LoginErrors.SignInAgain, await phone.ErrorOfAsync(phone.PostAsync(LoginPaths.Password, new { password = "second password" })));
        Assert.Equal(LoginErrors.SignInAgain, await phone.ErrorOfAsync(phone.PostAsync(LoginPaths.RecoveryCode)));

        Assert.Null(await phone.SignInAsync("Alice", "first password")); // a fresh session
        Assert.True((await phone.MeAsync()).RecentSignIn);
        Assert.Null(await phone.ErrorOfAsync(phone.PostAsync(LoginPaths.Password, new { password = "second password" })));

        Assert.Equal(LoginErrors.SignedOut, await laptop.ErrorOfAsync(laptop.GetAsync(LoginPaths.Me)));
        await phone.MeAsync();
        Assert.Equal(LoginErrors.WrongPassword, await laptop.SignInAsync("Alice", "first password"));
        Assert.Null(await laptop.SignInAsync("Alice", "second password"));
    }

    [Fact]
    public async Task ARecoveryCodeSignsInAloneAndRevokesOtherSessions()
    {
        var laptop = await ClaimedAsync("Alice", "a password");
        var code = (await laptop.PostAsync<RecoveryCodeResponse>(LoginPaths.RecoveryCode)).Code;
        Assert.Matches("^([0-9A-HJKMNP-TV-Z]{4}-){6}[0-9A-HJKMNP-TV-Z]{2}$", code);
        Assert.True((await laptop.MeAsync()).HasRecoveryCode);

        var phone = _host.NewClient();
        Assert.Null(await phone.SignInWithCodeAsync(code.Replace("-", " ").ToLowerInvariant()));
        Assert.Equal("Alice", (await phone.MeAsync()).Username);
        Assert.Equal(LoginErrors.SignedOut, await laptop.ErrorOfAsync(laptop.GetAsync(LoginPaths.Me)));

        // Then a new password, at once, since signing in with the code made a fresh session.
        Assert.Null(await phone.ErrorOfAsync(phone.PostAsync(LoginPaths.Password, new { password = "a new password" })));

        // The code can be used again.
        var tablet = _host.NewClient();
        Assert.Null(await tablet.SignInWithCodeAsync(code));
    }

    [Fact]
    public async Task ANewRecoveryCodeVoidsTheOld()
    {
        var alice = await ClaimedAsync("Alice", "a password");
        var old = (await alice.PostAsync<RecoveryCodeResponse>(LoginPaths.RecoveryCode)).Code;
        var current = (await alice.PostAsync<RecoveryCodeResponse>(LoginPaths.RecoveryCode)).Code;

        var other = _host.NewClient();
        Assert.Equal(LoginErrors.WrongCode, await other.SignInWithCodeAsync(old));
        Assert.Null(await other.SignInWithCodeAsync(current));
    }

    [Fact]
    public async Task GuestsCantMakeRecoveryCodes()
    {
        var guest = await _host.GuestAsync("Alice");
        Assert.Equal(LoginErrors.NotClaimed, await guest.ErrorOfAsync(guest.PostAsync(LoginPaths.RecoveryCode)));
    }

    [Fact]
    public async Task LockoutIsPerAccountAndAddressAndRecoveryCodesIgnoreIt()
    {
        var alice = await ClaimedAsync("Alice", "a password");
        var code = (await alice.PostAsync<RecoveryCodeResponse>(LoginPaths.RecoveryCode)).Code;

        var attacker = _host.NewClient("203.0.113.9");
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(LoginErrors.WrongPassword, await attacker.SignInAsync("Alice", "guess"));
        }

        Assert.Equal(LoginErrors.LockedOut, await attacker.SignInAsync("Alice", "a password"));

        var home = _host.NewClient("198.51.100.4");
        Assert.Null(await home.SignInAsync("Alice", "a password"));

        var lockedPlace = _host.NewClient("203.0.113.9");
        Assert.Null(await lockedPlace.SignInWithCodeAsync(code));

        _host.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Null(await attacker.SignInAsync("Alice", "a password"));
    }

    [Fact]
    public async Task ATicketNamesTheCharacterAndGameServer()
    {
        var guest = await _host.GuestAsync("Alice");
        var ticket = await guest.PostAsync<TicketResponse>(LoginPaths.Ticket);
        Assert.Equal(TestRules.ServerAddress, ticket.Server);

        var checker = new TicketChecker(TicketKeys.PublicKeysIn(_host.PublicKeys), TestRules.ServerId, _host.Clock);
        var (checkedTicket, rejection) = await checker.CheckAsync(ticket.Ticket);
        Assert.Null(rejection);

        await using var region = _host.Region();
        var character = await region.Characters.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(character.Id, checkedTicket!.CharacterId);
        Assert.Equal(character.AccountId, checkedTicket.AccountId);
    }

    [Fact]
    public async Task ABanRefusesSignInsAndKicksTwice()
    {
        var alice = await ClaimedAsync("Alice", "a password");
        var (accountId, characterId) = await IdsAsync("Alice");
        await ClaimAsync(characterId);

        await _host.Actions.BanAsync(accountId, TestContext.Current.CancellationToken);
        Assert.Equal(2, await OwnerVersionAsync(characterId));
        Assert.Equal(LoginErrors.Banned, await alice.ErrorOfAsync(alice.GetAsync(LoginPaths.Me)));
        Assert.Equal(LoginErrors.Banned, await alice.ErrorOfAsync(alice.PostAsync(LoginPaths.Ticket)));
        Assert.Equal(LoginErrors.Banned, await _host.NewClient().SignInAsync("Alice", "a password"));

        // The second kick, just after a ticket's lifetime.
        _host.Clock.Advance(TimeSpan.FromSeconds(62));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (await OwnerVersionAsync(characterId) < 3 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Equal(3, await OwnerVersionAsync(characterId));
    }

    [Fact]
    public async Task DeletingKicksAndHoldsTheNameAndRestoringBringsItBack()
    {
        var alice = await ClaimedAsync("Alice", "a password");
        var code = (await alice.PostAsync<RecoveryCodeResponse>(LoginPaths.RecoveryCode)).Code;
        var (accountId, characterId) = await IdsAsync("Alice");
        await ClaimAsync(characterId);

        await _host.Actions.DeleteAsync(accountId, TestContext.Current.CancellationToken);

        Assert.Equal(2, await OwnerVersionAsync(characterId));
        Assert.Equal(LoginErrors.SignedOut, await alice.ErrorOfAsync(alice.GetAsync(LoginPaths.Me)));
        var other = _host.NewClient();
        Assert.Equal(LoginErrors.NameTaken, await other.ErrorOfAsync(other.PostAsync(LoginPaths.Guest, new { name = "Alice", character = new { look = 1 } })));
        Assert.Equal(LoginErrors.AccountDeleted, await other.SignInAsync("Alice", "a password"));
        Assert.Equal(LoginErrors.AccountDeleted, await other.SignInWithCodeAsync(code));

        Assert.Null(await other.SignInAsync("Alice", "a password", LoginPaths.Restore));
        var me = await other.MeAsync();
        Assert.Equal("Alice", me.Character!.Name);
    }

    [Fact]
    public async Task RestoringWithARecoveryCodeWorksToo()
    {
        var alice = await ClaimedAsync("Alice", "a password");
        var code = (await alice.PostAsync<RecoveryCodeResponse>(LoginPaths.RecoveryCode)).Code;
        var (accountId, _) = await IdsAsync("Alice");
        await _host.Actions.DeleteAsync(accountId, TestContext.Current.CancellationToken);

        var other = _host.NewClient();
        var response = await other.PostAsync(LoginPaths.Restore, new { recoveryCode = code });
        response.EnsureSuccessStatusCode();
        other.Token = (await response.Content.ReadFromJsonAsync<SessionResponse>(TestContext.Current.CancellationToken))!.Token;
        Assert.NotNull((await other.MeAsync()).Character);
    }

    [Fact]
    public async Task TheCleanupDeletesOnlyIdleGuests()
    {
        await _host.GuestAsync("Idle");
        await _host.GuestAsync("Playing");
        await ClaimedAsync("Claimed", "a password");
        var fresh = await _host.GuestAsync("Fresh");
        var (_, playing) = await IdsAsync("Playing");

        _host.Clock.Advance(TimeSpan.FromDays(6));
        await ClaimAsync(playing); // a game server's heartbeat stamps the owner row
        await fresh.MeAsync(); // using the session counts as activity
        _host.Clock.Advance(TimeSpan.FromDays(2));

        Assert.Equal(1, await _host.Actions.CleanUpGuestsAsync(TestContext.Current.CancellationToken));
        await using var accounts = _host.Accounts();
        var deleted = await accounts.Accounts.Where(a => a.DeletedAt != null).Select(a => a.UserName).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["Idle"], deleted);

        // A guest can't restore itself, so the next purge frees the name without waiting out the hold.
        Assert.Equal((1, 1), await _host.Actions.PurgeAsync(TestContext.Current.CancellationToken));
        await _host.GuestAsync("Idle");
    }

    [Fact]
    public async Task CorsLetsThePageSendTheToken()
    {
        var client = _host.NewClient();
        var preflight = new HttpRequestMessage(HttpMethod.Options, LoginPaths.Me);
        preflight.Headers.Add("Origin", "https://page.test");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization");
        var response = await client.Http.SendAsync(preflight, TestContext.Current.CancellationToken);

        Assert.Equal("https://page.test", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("authorization", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")), StringComparison.OrdinalIgnoreCase);

        var stranger = new HttpRequestMessage(HttpMethod.Options, LoginPaths.Me);
        stranger.Headers.Add("Origin", "https://evil.test");
        stranger.Headers.Add("Access-Control-Request-Method", "GET");
        Assert.False((await client.Http.SendAsync(stranger, TestContext.Current.CancellationToken)).Headers.Contains("Access-Control-Allow-Origin"));
    }

    private async Task<LoginHost.Client> ClaimedAsync(string name, string password)
    {
        var client = await _host.GuestAsync(name);
        Assert.Null(await client.ErrorOfAsync(client.PostAsync(LoginPaths.Password, new { password })));
        return client;
    }

    private async Task<(long Account, long Character)> IdsAsync(string name)
    {
        await using var region = _host.Region();
        var character = await region.Characters.SingleAsync(c => c.Name == name, TestContext.Current.CancellationToken);
        return (character.AccountId, character.Id);
    }

    // What a game server's claim does to the owner row (version 1, held).
    private async Task ClaimAsync(long characterId)
    {
        await using var region = _host.Region();
        var owner = await region.CharacterOwners.SingleOrDefaultAsync(o => o.CharacterId == characterId, TestContext.Current.CancellationToken);
        if (owner == null)
        {
            region.CharacterOwners.Add(new() { CharacterId = characterId, ServerId = TestRules.ServerId, Version = 1 });
        }
        else
        {
            owner.UpdatedAt = _host.Clock.GetUtcNow().UtcDateTime;
        }

        await region.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<long> OwnerVersionAsync(long characterId)
    {
        await using var region = _host.Region();
        return await region.CharacterOwners.Where(o => o.CharacterId == characterId).Select(o => o.Version).SingleAsync();
    }
}

public sealed class RateLimitTests(PostgresContainer postgres)
{
    [Fact]
    public async Task SignInsAreLimitedPerAddressExceptExemptOnes()
    {
        await using var host = await LoginHost.StartAsync(postgres, options =>
        {
            options.RateLimits.SignInPerMinute = 5;
            options.RateLimits.ExemptAddresses = ["192.0.2.50"];
        });

        var limited = host.NewClient("203.0.113.9");
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(LoginErrors.WrongPassword, await limited.SignInAsync($"Nobody{i}", "guess"));
        }

        Assert.Equal(LoginErrors.RateLimited, await limited.SignInAsync("Nobody", "guess"));
        Assert.Equal(LoginErrors.WrongPassword, await host.NewClient("198.51.100.4").SignInAsync("Nobody", "guess"));

        var exempt = host.NewClient("192.0.2.50");
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(LoginErrors.WrongPassword, await exempt.SignInAsync("Nobody", "guess"));
        }
    }

    [Fact]
    public async Task OnlyTheLocalProxysForwardedAddressIsUsed()
    {
        await using var host = await LoginHost.StartAsync(postgres, options => options.RateLimits.SignInPerMinute = 1);

        // Through the local proxy (127.0.0.1): each forwarded client gets its own limit.
        foreach (var client in new[] { "203.0.113.1", "203.0.113.2" })
        {
            var viaProxy = host.NewClient();
            viaProxy.Http.DefaultRequestHeaders.Add("X-Forwarded-For", client);
            Assert.Equal(LoginErrors.WrongPassword, await viaProxy.SignInAsync("Nobody", "guess"));
        }

        // From anywhere else, the header is ignored: both requests count against the sender.
        for (var i = 0; i < 2; i++)
        {
            var spoofer = host.NewClient("198.51.100.7");
            spoofer.Http.DefaultRequestHeaders.Add("X-Forwarded-For", $"203.0.113.{10 + i}");
            var error = await spoofer.SignInAsync("Nobody", "guess");
            Assert.Equal(i == 0 ? LoginErrors.WrongPassword : LoginErrors.RateLimited, error);
        }
    }
}
