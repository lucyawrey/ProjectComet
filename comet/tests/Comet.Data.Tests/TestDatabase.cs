using Comet.Data.Characters;
using Microsoft.EntityFrameworkCore;

[assembly: AssemblyFixture(typeof(Comet.Testing.PostgresContainer))]

namespace Comet.Data.Tests;

public class TestCharacter : Character
{
    public required int Look { get; set; }
}

public sealed class TestRegionContext(DbContextOptions<TestRegionContext> options) : CometRegionContext<TestCharacter>(options);
