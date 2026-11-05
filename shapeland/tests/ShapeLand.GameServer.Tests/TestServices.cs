using Comet.Data.Characters;
using Comet.Tickets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ShapeLand.Data;

namespace ShapeLand.GameServer.Tests;

/// <summary>What a test server needs in place of PostgreSQL and the Login server's keys.</summary>
internal static class TestServices
{
    /// <summary>An empty in-memory character repository and a throwaway ticket key, for tests that never join.</summary>
    public static void AddInMemoryCharacters(WebApplicationBuilder builder)
    {
        var (_, publicPem) = TicketKeys.Create();
        builder.Services.AddSingleton<ICharacterRepository<ShapeLandCharacter>>(
            new InMemoryCharacterRepository<ShapeLandCharacter>(
                new InMemoryCharacterDatabase<ShapeLandCharacter>(TimeProvider.System),
                new CharacterRepositoryOptions { ServerId = 1 }
            )
        );
        builder.Services.AddSingleton(new TicketChecker([TicketKeys.PublicKey(publicPem)], 1, TimeProvider.System));
    }
}
