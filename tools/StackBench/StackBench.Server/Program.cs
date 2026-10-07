using System.Runtime;
using Comet.Server;
using Comet.Server.Connections;
using Comet.Server.Ticking;
using Microsoft.Extensions.Options;
using StackBench.Server;

// Stack benchmark server: bare Kestrel WebSockets, a fixed-rate tick loop and Comet's message
// layer, with no game logic. See .claude/design/prototype.md (Stack benchmark).
// Settings load from the build output, so the server can be started from any folder.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.Configure<BenchOptions>(builder.Configuration.GetSection("Bench"));
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<BenchGame>();
builder.Services.AddSingleton<IConnectionHandler>(services => services.GetRequiredService<BenchGame>());
builder.Services.AddSingleton(services => new TickLoop(
    services.GetRequiredService<IOptions<BenchOptions>>().Value.TickRate,
    services.GetRequiredService<BenchGame>().Tick));
builder.Services.AddSingleton<ServerMeasurement>();

var app = builder.Build();

app.UseWebSockets();
app.MapCometWebSocket("/ws");

var tickLoop = app.Services.GetRequiredService<TickLoop>();
app.Services.GetRequiredService<ServerMeasurement>();
var bench = app.Services.GetRequiredService<IOptions<BenchOptions>>().Value;
// Settle startup objects before bots connect, so their promotion isn't a pause in the window.
if (bench.SettleHeap)
{
    app.Lifetime.ApplicationStarted.Register(Heap.Settle);
}
if (!string.IsNullOrEmpty(bench.GcLatencyMode))
{
    var latencyMode = Enum.Parse<GCLatencyMode>(bench.GcLatencyMode);
    app.Lifetime.ApplicationStarted.Register(() => GCSettings.LatencyMode = latencyMode);
}
app.Lifetime.ApplicationStarted.Register(tickLoop.Start);
app.Lifetime.ApplicationStopping.Register(tickLoop.Stop);

app.Run();
