using Comet.Server.Diagnostics;

namespace Comet.Server.Tests;

public class GcPauseListenerTests
{
    [Fact]
    public async Task RecordsAPauseForAnInducedCollection()
    {
        var recorded = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new GcPauseListener();
        listener.PauseRecorded += pause => recorded.TrySetResult(pause);

        // Events arrive on the runtime's event thread, a little after the collection.
        var timeout = Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        while (!recorded.Task.IsCompleted && !timeout.IsCompleted)
        {
            GC.Collect();
            await Task.WhenAny(recorded.Task, Task.Delay(100, TestContext.Current.CancellationToken));
        }

        Assert.True(recorded.Task.IsCompleted, "No GC pause was recorded.");
        Assert.InRange(await recorded.Task, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }
}
