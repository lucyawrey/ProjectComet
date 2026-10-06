# Stack benchmark

Stage 1 of the load test: Comet's message layer on bare Kestrel WebSockets, with no game logic. It tells us early whether the stack (.NET, Kestrel, WebSocket, MessagePack) can carry the planned traffic. The design and thresholds are in `.claude/design/prototype.md` (Stack benchmark).

| Project | What it does |
| --- | --- |
| `StackBench.Server` | A 30 Hz tick loop; one entity per bot, positioned from the bot's reports; each tick sends every bot about 10 entity updates. Measures tick times, GC pauses, CPU, allocation and bandwidth. |
| `StackBench.Bots` | 300 WebSocket bots in one process. Each walks a seeded random path, reports its position at 15 Hz and pings twice a second. Measures round trips, gaps between updates and failures. |
| `StackBench.Report` | Checks both results files against `thresholds.json` and prints pass or fail. |
| `StackBench.Metrics` | Shared result types and HdrHistogram recording. |

## Running locally (no simulated network)

Run each command in a separate terminal (the paths below assume the repository root). Results go to `tools/StackBench/results/` (gitignored); relative results paths are resolved from the repository root.

```sh
dotnet run -c Release --project tools/StackBench/StackBench.Server
dotnet run -c Release --project tools/StackBench/StackBench.Bots
dotnet run -c Release --project tools/StackBench/StackBench.Report -- \
  tools/StackBench/results/server.json tools/StackBench/results/bots.json tools/StackBench/thresholds.json clean
```

A full run is a 1-minute warmup and a 10-minute window. For a quick check, shorten both on each side:

```sh
dotnet run -c Release --project tools/StackBench/StackBench.Server -- --Bench:WarmupSeconds=10 --Bench:DurationSeconds=30
dotnet run -c Release --project tools/StackBench/StackBench.Bots -- --warmup 10 --duration 30 --bots 50
```

The server's window starts a warmup after its first connection; the bots' window starts a warmup after they launch. Start the bots right after the server so the two line up.

Official runs use Docker with simulated network conditions (clean and impaired); that setup comes next.
