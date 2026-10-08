#:project ../comet/src/Comet.Client/Comet.Client.csproj
#:project ../shapeland/src/ShapeLand.Shared/ShapeLand.Shared.csproj
// Watches how other players move as a client sees them: joins a ShapeLand server as a player standing still,
// records every EntityState it receives (stamp, arrival time, position) and samples the interpolation buffer at
// 60 Hz like a client drawing, then prints per-entity statistics. Speed implied by stamps that varies more than
// speed by arrival points at stamp jitter; starved frames (drawn past the newest state) point at the buffer
// running dry. It also prints the delay each move needed (the next state's stamp age on arrival plus the gap
// since the previous stamp) against the session's adaptive interpolation delay.
//
//   dotnet run tools/observe.cs -- [--url ws://localhost:5080/ws] [--seconds 20]
//
// Needs the content built (artifacts/content/shapeland/content.bin) and players moving (bots, for instance).
using System.Diagnostics;
using Comet.Protocol;
using System.Numerics;
using Comet.Client;
using Comet.Protocol.Framing;
using Comet.Protocol.Messages;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;

var url = new Uri(Arg("--url", "ws://localhost:5080/ws"));
var seconds = double.Parse(Arg("--seconds", "20"));
var repo = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? "tools", ".."));
var content = ShapeLandContent.Load(File.OpenRead(Path.Combine(repo, "artifacts", "content", "shapeland", "content.bin")));
var clock = Stopwatch.StartNew();
ClientSession? sessionRef = null;
var peek = new Peek(new WebSocketTransport(url), () => clock.Elapsed.TotalSeconds, () => sessionRef is { Clock.Synced: true } ? sessionRef.Clock.ReceiveTick(clock.Elapsed.TotalSeconds) : double.NaN);
var session = sessionRef = new ClientSession(peek, ShapeLandProtocol.Options, teleportSpeed: ShapeLandWorld.TeleportSpeed(content));
session.GameMessage += (messageId, payload, tick) =>
{
    // The game registers other players with the buffer as they spawn, as the Unity client and bots do.
    if (messageId == ShapeLandMessageIds.PlayerSpawn && session.Welcomed)
    {
        var spawn = FrameReader.Decode<PlayerSpawn>(payload, ShapeLandProtocol.Options);
        if (spawn.EntityId != session.EntityId)
        {
            session.Entities.Spawn(spawn.EntityId, tick, new Vector3(spawn.X, spawn.Y, spawn.Z), spawn.Facing);
        }
    }
};
var asked = false;
var samples = new Dictionary<uint, List<(double Now, Vector3 Pos, bool Starved, double Past)>>();
while (clock.Elapsed.TotalSeconds < seconds)
{
    var now = clock.Elapsed.TotalSeconds;
    if (!asked && peek.State == TransportState.Open)
    {
        asked = true;
        session.Write(ShapeLandMessageIds.JoinRequest, new JoinRequest { Name = "Observer", Shape = content.Shapes.All[0].Number, Colour = ShapeLandRules.BodyColours[0], EyeColour = ShapeLandRules.EyeColours[0] });
    }
    session.Update(now);
    if (session.Welcomed && session.Clock.Synced && now > 3)
    {
        var render = session.RenderTick(now);
        foreach (var id in session.Entities.Ids)
        {
            if (session.Entities.TrySample(id, render, out var pose))
            {
                var newest = peek.States.TryGetValue(id, out var s) && s.Count > 0 ? s[^1].Tick : 0;
                (samples.TryGetValue(id, out var l) ? l : samples[id] = new()).Add((now, pose.Position, render > newest, (render - newest) * 1000.0 / session.Clock.TickRate));
            }
        }
    }
    session.Flush(now);
    Thread.Sleep(16);
}

Console.WriteLine($"round trip {session.Clock.RoundTrip * 1000:0.0} ms; interpolation delay {session.InterpolationDelay.Seconds * 1000:0} ms (target {session.InterpolationDelay.Target * 1000 / 30:0}); {peek.States.Count} players moving, {session.StatesReceived} states received");
foreach (var (id, states) in peek.States)
{
    if (states.Count < 10 || !samples.TryGetValue(id, out var frames)) continue;
    // Raw states: interval between stamps, arrival gaps, and speed implied by stamps (constant for a bot walking straight).
    var stampGaps = new List<double>(); var arrivalGaps = new List<double>(); var speeds = new List<double>(); var arrivalSpeeds = new List<double>();
    for (var i = 1; i < states.Count; i++)
    {
        var a = states[i - 1]; var b = states[i];
        var dt = (b.Tick - a.Tick) / 30.0; var da = b.Arrived - a.Arrived;
        var d = Vector2.Distance(new(a.Pos.X, a.Pos.Z), new(b.Pos.X, b.Pos.Z));
        stampGaps.Add(dt * 1000); arrivalGaps.Add(da * 1000);
        if (dt > 0 && d > 0.05 && dt < 0.2) { speeds.Add(d / dt); if (da > 0.001) arrivalSpeeds.Add(d / da); }
    }
    // Drawn: per-frame ground speed; starved frames.
    var drawn = new List<double>();
    for (var i = 1; i < frames.Count; i++)
    {
        var dt = frames[i].Now - frames[i - 1].Now;
        var d = Vector2.Distance(new(frames[i - 1].Pos.X, frames[i - 1].Pos.Z), new(frames[i].Pos.X, frames[i].Pos.Z));
        if (dt > 0) drawn.Add(d / dt);
    }
    var moving = drawn.Where(v => v > 0.5).ToList();
    var still = drawn.Count(v => v < 0.05);
    Console.WriteLine($"entity {id}: {states.Count} states; stamp gap ms {Stats(stampGaps)}; arrival gap ms {Stats(arrivalGaps)}");
    Console.WriteLine($"   speed by stamps m/s {Stats(speeds)}; by arrival {Stats(arrivalSpeeds)}");
    // Stamp age on arrival: how far behind the receive timeline (frame ticks) each state's stamp is. The
    // interpolation delay has to cover this plus the gap to the next state, or the buffer runs dry.
    var ages = states.Where(x => !double.IsNaN(x.ReceiveTick)).Select(x => (x.ReceiveTick - x.Tick) * 1000.0 / 30).ToList();
    var past = frames.Where(f => f.Starved).Select(f => f.Past).ToList();
    // While moving (stamp gaps up to the idle gap, 200 ms), the buffer never runs dry when the interpolation
    // delay covers the next state's stamp age on arrival plus the gap since the previous stamp.
    var needed = new List<double>();
    for (var i = 1; i < states.Count; i++)
    {
        var gap = (states[i].Tick - states[i - 1].Tick) * 1000.0 / 30;
        if (gap <= 200 && !double.IsNaN(states[i].ReceiveTick)) needed.Add((states[i].ReceiveTick - states[i - 1].Tick) * 1000.0 / 30);
    }
    needed.Sort();
    Console.WriteLine($"   delay needed while moving ms: median {Pct(needed, 0.5):0} 95% {Pct(needed, 0.95):0} 99% {Pct(needed, 0.99):0} max {Pct(needed, 1):0}; under the delay at the end {needed.Count(x => x > session.InterpolationDelay.Seconds * 1000) * 100.0 / Math.Max(1, needed.Count):0.0}% of moves run dry");
    Console.WriteLine($"   stamp age on arrival ms {Stats(ages)}; render past newest ms (starved frames) {Stats(past)}");
    Console.WriteLine($"   drawn frames {frames.Count}: starved {frames.Count(f => f.Starved) * 100.0 / frames.Count:0.0}%, stopped {still * 100.0 / drawn.Count:0.0}%, moving speed m/s {Stats(moving)}");
}
session.Transport.Close();

string Arg(string name, string fallback)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

static double Pct(List<double> sorted, double p) => sorted.Count == 0 ? double.NaN : sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];

static string Stats(List<double> v) => v.Count == 0 ? "-" : $"mean {v.Average():0.00} sd {Math.Sqrt(v.Select(x => (x - v.Average()) * (x - v.Average())).Average()):0.00} min {v.Min():0.00} max {v.Max():0.00}";

sealed class Peek(IClientTransport inner, Func<double> now, Func<double> receiveTick) : IClientTransport
{
    public readonly Dictionary<uint, List<(double Arrived, uint Tick, Vector3 Pos, double ReceiveTick)>> States = new();
    public TransportState State => inner.State;
    public string? Error => inner.Error;
    public void Send(ReadOnlySpan<byte> frame) => inner.Send(frame);
    public void Close() => inner.Close();
    public void Dispose() => inner.Dispose();
    public bool TryReceive(out byte[] frame)
    {
        if (!inner.TryReceive(out frame)) return false;
        var reader = FrameReader.Create(frame);
        while (reader.TryReadNext(out var id, out var payload))
        {
            if (id != MessageIds.EntityState) continue;
            var s = FrameReader.Decode<EntityState>(payload, ShapeLandProtocol.Options);
            (States.TryGetValue(s.EntityId, out var l) ? l : States[s.EntityId] = new()).Add((now(), s.Tick, new Vector3(s.X, s.Y, s.Z), receiveTick()));
        }
        return true;
    }
}
