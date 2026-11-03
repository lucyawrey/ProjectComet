// The shared development PostgreSQL: one container, comet-postgres, for every local stack (the dev tool, the
// editor's local server, Unity's play-mode tests, each worktree), each with its own database in it. It listens on
// 127.0.0.1:55432 (apart from a PostgreSQL of your own on 5432), keeps its data in the comet-postgres-data volume,
// and stays running. It's also on the Docker network "comet", where containers (the --net runs) reach it as
// comet-postgres:5432. The password is fixed and local only. .NET tests don't use it: they start their own.
//
//   tools/run comet-postgres start                 start it if it isn't running, and wait until it answers
//   tools/run comet-postgres database NAME         start it, make the database NAME if missing, and print its
//                                                 connection string (for programs on this machine)
//   tools/run comet-postgres fresh NAME            the same, emptying the database first
//   tools/run comet-postgres drop NAME             drop the database NAME
//   tools/run comet-postgres name CHECKOUT PORT    a database name for a stack: the checkout's folder and its port
#:property NoWarn=CA2266
#:property IncludeProjectNameInArtifactsPaths=true
#:project shared/Tools.Shared.csproj
using System.Text.RegularExpressions;

args = Tool.Arguments(args);

const string Container = "comet-postgres", Volume = "comet-postgres-data", Network = "comet", User = "comet",
    Password = "comet-dev-only";
const int Port = 55432;

var command = args.FirstOrDefault();
switch (command)
{
    case "start":
        Start();
        break;
    case "database" or "fresh":
        var name = DatabaseName();
        Start();
        if (command == "fresh") Psql($"DROP DATABASE IF EXISTS {name} WITH (FORCE)");
        // The comet user owns it, as the Login server needs (its migrations make a collation in public).
        if (Psql($"SELECT 1 FROM pg_database WHERE datname = '{name}'", tuples: true) != "1")
            Psql($"CREATE DATABASE {name}");
        Console.WriteLine($"Host=127.0.0.1;Port={Port};Database={name};Username={User};Password={Password}");
        break;
    case "drop":
        var dropped = DatabaseName();
        Start();
        Psql($"DROP DATABASE IF EXISTS {dropped} WITH (FORCE)");
        break;
    case "name" when args.Length == 3:
        var folder = Regex.Replace(Path.GetFileName(Path.TrimEndingDirectorySeparator(args[1])).ToLowerInvariant(), "[^a-z0-9]", "_");
        Console.WriteLine($"shapeland_{folder}_{args[2]}");
        break;
    case "-h" or "--help":
        Tool.Usage();
        break;
    default:
        Tool.Usage(2);
        break;
}

string DatabaseName()
{
    var name = args.ElementAtOrDefault(1) ?? throw Tool.Fail("Give a database name.", 2);
    return Regex.IsMatch(name, "^[a-z_][a-z0-9_]*$") ? name
        : throw Tool.Fail($"Database names here are lowercase letters, digits and '_': {name}", 2);
}

void Start()
{
    if (!Tool.Has("docker")) throw Tool.Fail("Docker is needed for local play with accounts (PostgreSQL runs in it).");
    if (Tool.Capture("docker", "network", "inspect", Network).Code != 0)
        Tool.Output("docker", "network", "create", Network);
    if (Tool.Output("docker", "ps", "-q", "--filter", $"name=^{Container}$") == "")
    {
        if (Tool.Output("docker", "ps", "-aq", "--filter", $"name=^{Container}$") != "")
            Tool.Output("docker", "start", Container);
        else
            Tool.Output("docker", "run", "-d", "--name", Container, "--restart", "unless-stopped", "--network", Network,
                "-p", $"127.0.0.1:{Port}:5432", "-v", $"{Volume}:/var/lib/postgresql",
                "-e", $"POSTGRES_USER={User}", "-e", $"POSTGRES_PASSWORD={Password}", "postgres:18");
    }
    if (!Tool.WaitFor(() => Tool.Capture(Tool.Info("docker", ["exec", Container, "pg_isready", "-q", "-U", User]), withErrors: true).Code == 0,
            60, TimeSpan.FromMilliseconds(500)))
        throw Tool.Fail($"comet-postgres didn't start; see docker logs {Container}.");
}

string Psql(string sql, bool tuples = false) =>
    Tool.Output("docker", ["exec", Container, "psql", "-q", "-v", "ON_ERROR_STOP=1", "-U", User, "-d", "postgres",
        .. tuples ? new[] { "-tA" } : [], "-c", sql]);
