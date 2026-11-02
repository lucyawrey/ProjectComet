using Comet.Data.Characters;

namespace Comet.Data.Tests;

/// <summary>What a development server without accounts adds to the in-memory database: names and the snapshot file.</summary>
public sealed class InMemoryCharacterDatabaseTests : IDisposable
{
    private readonly string _snapshot = Path.Combine(Path.GetTempPath(), $"comet-snapshot-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_snapshot);
        File.Delete(_snapshot + ".bad");
    }

    private static TestCharacter Make(long id, string name, int look = 0) => new() { Id = id, AccountId = 1, Name = name, Look = look };

    [Fact]
    public void FindOrAddIgnoresCaseAndGivesTheNextFreeId()
    {
        var database = new InMemoryCharacterDatabase<TestCharacter>(TimeProvider.System);
        database.Add(Make(41, "Ada"));

        Assert.Equal((41, false), database.FindOrAdd("ADA", id => Make(id, "ADA")));
        Assert.Equal((42, true), database.FindOrAdd("Bea", id => Make(id, "Bea")));
    }

    [Fact]
    public void TheSnapshotKeepsCharactersAndSpotsButNoHolds()
    {
        var database = new InMemoryCharacterDatabase<TestCharacter>(TimeProvider.System);
        Assert.Null(database.Load(_snapshot)); // no file yet: starts empty
        var (id, _) = database.FindOrAdd("Ada", id => Make(id, "Ada", look: 7));
        database.SetPosition(id, new CharacterPosition(3, 1, 2, 3, 0.5f));

        var reloaded = new InMemoryCharacterDatabase<TestCharacter>(TimeProvider.System);
        Assert.Null(reloaded.Load(_snapshot));
        Assert.Equal(7, reloaded.Character(id)!.Look);
        Assert.Equal(new CharacterPosition(3, 1, 2, 3, 0.5f), reloaded.Position(id));
        Assert.Null(reloaded.Owner(id));
    }
}
