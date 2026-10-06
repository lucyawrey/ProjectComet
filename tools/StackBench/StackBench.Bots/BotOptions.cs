namespace StackBench.Bots;

public sealed class BotOptions
{
    public Uri Url { get; private set; } = new("ws://localhost:5080/ws");
    public int Bots { get; private set; } = 300;

    /// <summary>Position reports per second.</summary>
    public double ReportRate { get; private set; } = 15;

    /// <summary>Pings per second, for round-trip times.</summary>
    public double PingRate { get; private set; } = 2;

    /// <summary>Bots connect evenly spread over this many seconds, within the warmup.</summary>
    public int RampSeconds { get; private set; } = 10;

    public int WarmupSeconds { get; private set; } = 60;
    public int DurationSeconds { get; private set; } = 600;
    public int Seed { get; private set; } = 1;
    public string ResultsPath { get; private set; } = "tools/StackBench/results/bots.json";

    public const string Usage = """
        Usage: StackBench.Bots [options]
          --url <ws://host:port/ws>   Server endpoint (default ws://localhost:5080/ws)
          --bots <n>                  Number of bots (default 300)
          --report-rate <hz>          Position reports per second (default 15)
          --ping-rate <hz>            Pings per second (default 2)
          --ramp <s>                  Spread connections over this many seconds (default 10)
          --warmup <s>                Seconds before the measurement window (default 60)
          --duration <s>              Measurement window length (default 600)
          --seed <n>                  Random seed for bot movement (default 1)
          --results <path>            Results file (default tools/StackBench/results/bots.json)
        """;

    public static BotOptions Parse(string[] args)
    {
        var options = new BotOptions();
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {args[i]}.");
            }
            var value = args[i + 1];
            switch (args[i])
            {
                case "--url": options.Url = new Uri(value); break;
                case "--bots": options.Bots = int.Parse(value); break;
                case "--report-rate": options.ReportRate = double.Parse(value); break;
                case "--ping-rate": options.PingRate = double.Parse(value); break;
                case "--ramp": options.RampSeconds = int.Parse(value); break;
                case "--warmup": options.WarmupSeconds = int.Parse(value); break;
                case "--duration": options.DurationSeconds = int.Parse(value); break;
                case "--seed": options.Seed = int.Parse(value); break;
                case "--results": options.ResultsPath = value; break;
                default: throw new ArgumentException($"Unknown option {args[i]}.");
            }
        }
        if (options.RampSeconds >= options.WarmupSeconds)
        {
            throw new ArgumentException("The ramp must finish within the warmup.");
        }
        return options;
    }
}
