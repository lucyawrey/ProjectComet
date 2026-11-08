// ShapeLand's deployment: its page on Vercel (https://shapeland.lucyawrey.com) and its servers on an on-demand AWS
// instance (https://server.shapeland.lucyawrey.com: the Login server under /api/, the game server at /ws), up for
// as long as wanted and terminated after. Accounts outlive the instance: PostgreSQL keeps its data on a separate
// volume that down and redeploy keep, snapshotted daily (7 kept). Run it from your own machine; nothing here needs
// an agent.
//
//   tools/run shapeland-deploy setup                 once per AWS account: the S3 bucket, the firewall rules, the
//                                                    data volume and its daily snapshots
//   tools/run shapeland-deploy up [--hours 0] [--bots 0]   build and start the servers, and point the hostname at them
//   tools/run shapeland-deploy down                  terminate the servers (keeping the data volume) and park the hostname
//   tools/run shapeland-deploy status                what's running, and whether the server answers
//   tools/run shapeland-deploy log                   the server's boot log (from its console)
//   tools/run shapeland-deploy page [--no-build]     build the web page and publish it on Vercel
//   tools/run shapeland-deploy redeploy [options]    page, then up, ending a server that's already up first
//                                                    (after the new one is built, so it's down only briefly)
//
//   --bots N       start N honest bots on the server's instance too, so visitors see someone moving (default 0)
//   --hours H      the server shuts itself down (and is terminated) after H hours, so a forgotten one stops
//                  costing money; 0 for never (the default). `down` ends it sooner.
//   --no-build     publish the last web build as it is
//
// Needs: the AWS CLI logged in (`aws login`), the Vercel CLI logged in (`vercel login`); Docker isn't needed.
// Settings that aren't in the repo live in ~/.config/shapeland-deploy/env (created on first use):
//   ACME_EMAIL     the contact address Caddy gives Let's Encrypt and ZeroSSL for the server's certificate
// Costs: a t4g.micro is about 20 cents a day while up (paid from the account's credits first), the 8 GB data volume
// and its snapshots well under a dollar a month; the page is on Vercel's free plan. When the servers are down, the
// page says the game isn't open for testing.
#:project shared/Tools.Shared.csproj
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;

args = Tool.Arguments(args);

var work = Path.Combine(Tool.Repo, "artifacts", "deploy");
var web = Path.Combine(Tool.Repo, "artifacts", "unity", "shapeland", "web");
var content = Path.Combine(Tool.Repo, "artifacts", "content", "shapeland", "content.bin");
var configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "shapeland-deploy");

const string Domain = "lucyawrey.com",
    ServerName = "server.shapeland",
    InstanceType = "t4g.micro",
    Tag = "shapeland-deploy",
    VercelProject = "shapeland";
const string PageHost = "shapeland." + Domain,
    ServerHost = ServerName + "." + Domain;

// Where the server's name points while it's down: an address reserved for documentation, which nothing answers.
// Removing the record instead would let the domain's wildcard answer for the name, and resolvers keep that
// answer for 30 minutes, past the next up; the record's own cache time is a minute.
const string Parked = "192.0.2.1";

var options = new Tool.Args(args);
if (!options.More)
    Tool.Usage(2);
var command = options.Take();
int hours = 0,
    bots = 0;
var build = true;
while (options.More)
{
    switch (options.Take())
    {
        case "--hours":
            hours = options.Number("--hours");
            break;
        case "--bots":
            bots = options.Number("--bots");
            break;
        case "--no-build":
            build = false;
            break;
        case "-h" or "--help":
            Tool.Usage();
            break;
        case var other:
            throw Tool.Fail($"Unknown option: {other}", 2);
    }
}
if (command is "-h" or "--help")
    Tool.Usage();
if (command is not ("setup" or "up" or "down" or "status" or "log" or "page" or "redeploy"))
    throw Tool.Fail($"Unknown command: {command} (setup, up, down, status, log, page or redeploy)", 2);

var region = Tool.Capture("aws", "configure", "get", "region").Output.Trim();
if (region == "")
    throw Tool.Fail("No AWS region set: run aws configure set region us-east-2", 2);

switch (command)
{
    case "setup":
        Setup();
        break;
    case "up":
        Up(packaged: false);
        break;
    case "down":
        Down();
        break;
    case "status":
        Status();
        break;
    case "log":
        Log();
        break;
    case "page":
        Page();
        break;
    case "redeploy":
        Page();
        Package();
        if (Instances().Length > 0)
            Down();
        Up(packaged: true);
        break;
}

string Aws(params string[] awsArgs) => Tool.Output("aws", awsArgs);

string Account()
{
    var (code, text) = Tool.Capture(Tool.Info("aws", ["sts", "get-caller-identity", "--query", "Account", "--output", "text"]), withErrors: true);
    return code == 0 ? text.Trim() : throw Tool.Fail("The AWS CLI isn't logged in: run aws login", 2);
}

string Bucket() => $"{Tag}-{Account()}-{region}";

string AcmeEmail()
{
    var file = Path.Combine(configDir, "env");
    if (!File.Exists(file))
    {
        Directory.CreateDirectory(configDir);
        File.WriteAllText(file, "ACME_EMAIL=\n");
        throw Tool.Fail($"Fill in {file} (ACME_EMAIL, the certificate contact address) and run this again.", 2);
    }
    var email = File.ReadLines(file).Select(l => l.Split('=', 2)).FirstOrDefault(p => p[0].Trim() == "ACME_EMAIL")?[1].Trim().Trim('"');
    return string.IsNullOrEmpty(email) ? throw Tool.Fail($"Set ACME_EMAIL in {file}.", 2) : email;
}

// The running (or starting) game server instances' ids.
string[] Instances() =>
    Aws(
            "ec2",
            "describe-instances",
            "--filters",
            $"Name=tag:{Tag},Values=server",
            "Name=instance-state-name,Values=pending,running,stopping,stopped",
            "--query",
            "Reservations[].Instances[].InstanceId",
            "--output",
            "text"
        )
        .Split((char[])[' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries);

// The kept data volume's id, or None.
string DataVolume() =>
    Aws("ec2", "describe-volumes", "--filters", $"Name=tag:{Tag},Values=data", "--query", "Volumes[0].VolumeId", "--output", "text");

string SecurityGroup() =>
    Aws("ec2", "describe-security-groups", "--filters", $"Name=group-name,Values={Tag}", "--query", "SecurityGroups[0].GroupId", "--output", "text");

// Vercel DNS: the server's records as (id, value), from the CLI's table (columns: id, name, type, value, ...).
List<(string Id, string Value)> DnsRecords() =>
    Tool.Capture(Tool.Info("vercel", ["dns", "ls", Domain]), withErrors: false)
        .Output.Split('\n')
        .Select(l => l.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        .Where(f => f.Length >= 4 && f[1] == ServerName)
        .Select(f => (f[0], f[3]))
        .ToList();

void DnsSet(string address)
{
    foreach (var (id, _) in DnsRecords())
        Tool.Output("vercel", "dns", "rm", id, "--yes");
    Tool.Output("vercel", "dns", "add", Domain, ServerName, "A", address);
}

// Whether the game and Login servers at an address answer over HTTPS with "ok" from /health and /api/health. Asked
// at that address, so a stale DNS answer on this machine can't mislead it; the certificate is still checked for the name.
bool Answers(string address)
{
    using var http = new HttpClient(
        new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancel) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(IPAddress.Parse(address), 443, cancel);
                return new NetworkStream(socket, ownsSocket: true);
            },
        }
    )
    {
        Timeout = TimeSpan.FromSeconds(5),
    };
    foreach (var path in new[] { "health", "api/health" })
    {
        try
        {
            if (http.GetStringAsync($"https://{ServerHost}/{path}").GetAwaiter().GetResult() != "ok")
                return false;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or SocketException)
        {
            return false;
        }
    }
    return true;
}

// The running server's public address, if any.
string? ServerIp()
{
    var ids = Instances();
    return ids.Length == 0
        ? null
        : Aws(["ec2", "describe-instances", "--instance-ids", .. ids, "--query", "Reservations[0].Instances[0].PublicIpAddress", "--output", "text"]);
}

void Setup()
{
    var bucket = Bucket();
    if (Tool.Capture(Tool.Info("aws", ["s3api", "head-bucket", "--bucket", bucket]), withErrors: true).Code != 0)
    {
        Console.WriteLine($"== Creating the bucket {bucket} (private)");
        Aws([
            "s3api",
            "create-bucket",
            "--bucket",
            bucket,
            .. region == "us-east-1" ? Array.Empty<string>() : ["--create-bucket-configuration", $"LocationConstraint={region}"],
        ]);
        Aws(
            "s3api",
            "put-public-access-block",
            "--bucket",
            bucket,
            "--public-access-block-configuration",
            "BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true"
        );
        // Old packages go after a day; each up uploads its own.
        Aws(
            "s3api",
            "put-bucket-lifecycle-configuration",
            "--bucket",
            bucket,
            "--lifecycle-configuration",
            """{"Rules":[{"ID":"expire-packages","Status":"Enabled","Filter":{"Prefix":""},"Expiration":{"Days":1}}]}"""
        );
    }

    var group = SecurityGroup();
    if (group == "None")
    {
        Console.WriteLine($"== Creating the security group {Tag} (HTTP and HTTPS in; no SSH)");
        var vpc = Aws("ec2", "describe-vpcs", "--filters", "Name=isDefault,Values=true", "--query", "Vpcs[0].VpcId", "--output", "text");
        group = Aws(
            "ec2",
            "create-security-group",
            "--group-name",
            Tag,
            "--description",
            "ShapeLand: Caddy on 80 and 443",
            "--vpc-id",
            vpc,
            "--query",
            "GroupId",
            "--output",
            "text"
        );
        Aws(
            "ec2",
            "authorize-security-group-ingress",
            "--group-id",
            group,
            "--ip-permissions",
            "IpProtocol=tcp,FromPort=80,ToPort=80,IpRanges=[{CidrIp=0.0.0.0/0}],Ipv6Ranges=[{CidrIpv6=::/0}]",
            "IpProtocol=tcp,FromPort=443,ToPort=443,IpRanges=[{CidrIp=0.0.0.0/0}],Ipv6Ranges=[{CidrIpv6=::/0}]"
        );
    }

    var volume = DataVolume();
    if (volume == "None")
    {
        Console.WriteLine($"== Creating the data volume (8 GB, encrypted) in {region}a");
        volume = Aws(
            "ec2",
            "create-volume",
            "--availability-zone",
            $"{region}a",
            "--size",
            "8",
            "--volume-type",
            "gp3",
            "--encrypted",
            "--tag-specifications",
            $"ResourceType=volume,Tags=[{{Key={Tag},Value=data}},{{Key=Name,Value={Tag}-data}}]",
            "--query",
            "VolumeId",
            "--output",
            "text"
        );
    }
    var policies = Tool.Capture(
        Tool.Info("aws", ["dlm", "get-lifecycle-policies", "--target-tags", $"{Tag}=data", "--query", "length(Policies)", "--output", "text"]),
        withErrors: true
    );
    if (policies.Output.Trim() != "1")
    {
        Console.WriteLine("== Creating the daily snapshot policy (7 kept)");
        Tool.Capture(Tool.Info("aws", ["dlm", "create-default-role", "--resource-type", "snapshot"]), withErrors: true);
        var role = Aws("iam", "get-role", "--role-name", "AWSDataLifecycleManagerDefaultRole", "--query", "Role.Arn", "--output", "text");
        var details = $$"""
            {"PolicyType":"EBS_SNAPSHOT_MANAGEMENT","ResourceTypes":["VOLUME"],"TargetTags":[{"Key":"{{Tag}}","Value":"data"}],
             "Schedules":[{"Name":"daily","CreateRule":{"Interval":24,"IntervalUnit":"HOURS","Times":["09:00"]},
               "RetainRule":{"Count":7},"CopyTags":true}]}
            """;
        Aws(
            "dlm",
            "create-lifecycle-policy",
            "--description",
            $"{Tag} data daily",
            "--state",
            "ENABLED",
            "--execution-role-arn",
            role,
            "--policy-details",
            details
        );
    }
    Console.WriteLine($"Set up: bucket {bucket}, security group {group}, data volume {volume}.");
}

// The server package: the Login and game servers for Linux on ARM (self-contained), the content and the Caddyfile,
// and the bots when any are asked for.
void Package()
{
    var output = Path.Combine(work, "package");
    if (Directory.Exists(output))
        Directory.Delete(output, recursive: true);
    Directory.CreateDirectory(output);
    Console.WriteLine("== Building the game server package");
    Tool.Quietly("dotnet", "run", "--project", Path.Combine(Tool.Repo, "shapeland", "src", "ShapeLand.ContentBuild"), "--", "build", "--release");
    var programs = new List<(string Project, string Folder)> { ("ShapeLand.LoginServer", "login"), ("ShapeLand.GameServer", "server") };
    if (bots > 0)
        programs.Add(("ShapeLand.Bots", "bots"));
    foreach (var (project, folder) in programs)
        Tool.Quietly(
            "dotnet",
            "publish",
            Path.Combine(Tool.Repo, "shapeland", "src", project),
            "-c",
            "Release",
            "-r",
            "linux-arm64",
            "--self-contained",
            "-o",
            Path.Combine(output, folder)
        );
    File.Copy(content, Path.Combine(output, "content.bin"));
    File.Copy(Path.Combine(Tool.Repo, "shapeland", "deploy", "Caddyfile"), Path.Combine(output, "Caddyfile"));

    // Written here rather than with tar, so it's the same from every platform: the programs are executable on Linux
    // whatever file modes this machine keeps.
    using var file = File.Create(Path.Combine(work, "package.tar.gz"));
    using var gzip = new GZipStream(file, CompressionLevel.Optimal);
    using var tar = new TarWriter(gzip, TarEntryFormat.Pax);
    foreach (var path in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
    {
        var name = Path.GetRelativePath(output, path).Replace('\\', '/');
        var entry = new PaxTarEntry(TarEntryType.RegularFile, "./" + name)
        {
            Mode =
                programs.Any(p => name == $"{p.Folder}/{p.Project}") || name.EndsWith(".so", StringComparison.Ordinal)
                    ? (UnixFileMode)0b111_101_101
                    : (UnixFileMode)0b110_100_100,
            DataStream = File.OpenRead(path),
        };
        tar.WriteEntry(entry);
        entry.DataStream.Dispose();
    }
}

void Up(bool packaged)
{
    var acmeEmail = AcmeEmail();
    var existing = Instances();
    if (existing.Length > 0)
        throw Tool.Fail($"A game server is already up ({string.Join(' ', existing)}); run down first.");
    var group = SecurityGroup();
    var volume = DataVolume();
    if (group == "None" || volume == "None")
        throw Tool.Fail("Run setup first.");
    // The instance starts beside the volume, in its availability zone; the last instance must have let it go.
    Aws("ec2", "wait", "volume-available", "--volume-ids", volume);
    var zone = Aws("ec2", "describe-volumes", "--volume-ids", volume, "--query", "Volumes[0].AvailabilityZone", "--output", "text");
    var subnet = Aws(
        "ec2",
        "describe-subnets",
        "--filters",
        $"Name=availability-zone,Values={zone}",
        "Name=default-for-az,Values=true",
        "--query",
        "Subnets[0].SubnetId",
        "--output",
        "text"
    );

    if (!packaged)
        Package();
    var bucket = Bucket();
    var key = $"package-{DateTime.Now:yyyyMMdd-HHmmss}.tar.gz";
    Aws("s3", "cp", "--quiet", Path.Combine(work, "package.tar.gz"), $"s3://{bucket}/{key}");
    var url = Aws("s3", "presign", $"s3://{bucket}/{key}", "--expires-in", "3600");

    var boot = File.ReadAllText(Path.Combine(Tool.Repo, "shapeland", "deploy", "boot.sh"))
        .Replace("{{HOST}}", ServerHost)
        .Replace("{{PAGE_ORIGIN}}", $"https://{PageHost}")
        .Replace("{{VOLUME_ID}}", volume)
        .Replace("{{ACME_EMAIL}}", acmeEmail)
        .Replace("{{PACKAGE_URL}}", url)
        .Replace("{{HOURS}}", hours.ToString())
        .Replace("{{BOTS}}", bots.ToString())
        .ReplaceLineEndings("\n");
    var bootFile = Path.Combine(work, "boot.sh");
    File.WriteAllText(bootFile, boot);

    Console.WriteLine($"== Starting a {InstanceType} in {region}");
    var ami = Aws(
        "ssm",
        "get-parameter",
        "--name",
        "/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-arm64",
        "--query",
        "Parameter.Value",
        "--output",
        "text"
    );
    var id = Aws(
        "ec2",
        "run-instances",
        "--image-id",
        ami,
        "--instance-type",
        InstanceType,
        "--security-group-ids",
        group,
        "--subnet-id",
        subnet,
        "--user-data",
        $"file://{bootFile}",
        "--instance-initiated-shutdown-behavior",
        "terminate",
        "--metadata-options",
        "HttpTokens=required",
        "--tag-specifications",
        $"ResourceType=instance,Tags=[{{Key={Tag},Value=server}},{{Key=Name,Value={Tag}}}]",
        "--query",
        "Instances[0].InstanceId",
        "--output",
        "text"
    );
    Aws("ec2", "wait", "instance-running", "--instance-ids", id);
    Console.WriteLine($"== Attaching the data volume {volume}");
    Aws("ec2", "attach-volume", "--volume-id", volume, "--instance-id", id, "--device", "/dev/sdf");
    var ip = Aws("ec2", "describe-instances", "--instance-ids", id, "--query", "Reservations[0].Instances[0].PublicIpAddress", "--output", "text");

    Console.WriteLine($"== Pointing {ServerHost} at {ip}");
    DnsSet(ip);

    Console.WriteLine($"== Waiting for the server to answer at https://{ServerHost} (a few minutes: boot, then a certificate)");
    if (!Tool.WaitFor(() => Answers(ip), 60, TimeSpan.FromSeconds(10)))
        throw Tool.Fail($"The server didn't answer within 10 minutes; see {Tool.Command("shapeland-deploy")} log. It's still running: down ends it.");
    Console.WriteLine(
        $"Up: {id} at {ip}, with {bots} bots and the accounts on {volume}. Play at https://{PageHost}. "
            + (hours > 0 ? $"It shuts down by itself in {hours} hours; down ends it sooner." : "It runs until down.")
    );
}

void Down()
{
    var ids = Instances();
    Console.WriteLine($"== Parking {ServerHost}");
    DnsSet(Parked);
    if (ids.Length > 0)
    {
        Console.WriteLine($"== Terminating {string.Join(' ', ids)}");
        Aws(["ec2", "terminate-instances", "--instance-ids", .. ids]);
        Aws(["ec2", "wait", "instance-terminated", "--instance-ids", .. ids]);
    }
    Console.WriteLine("Down.");
}

void Status()
{
    var ids = Instances();
    Console.WriteLine(
        ids.Length == 0
            ? "No game server is up."
            : Aws([
                "ec2",
                "describe-instances",
                "--instance-ids",
                .. ids,
                "--query",
                "Reservations[].Instances[].[InstanceId,State.Name,PublicIpAddress,LaunchTime]",
                "--output",
                "text",
            ])
    );
    Console.WriteLine($"DNS: {ServerHost} -> {string.Join(' ', DnsRecords().Select(r => r.Value))}");
    var ip = ServerIp();
    Console.WriteLine(ip != null && Answers(ip) ? $"The server at {ip} answers." : "No server answers.");
}

void Log()
{
    var id = Instances().FirstOrDefault() ?? throw Tool.Fail("No game server is up.");
    Console.WriteLine(Aws("ec2", "get-console-output", "--instance-id", id, "--latest", "--output", "text"));
}

void Page()
{
    var output = Path.Combine(work, VercelProject);
    if (build)
    {
        Console.WriteLine("== Building the web page (Unity, a few minutes)");
        // Never a fast dev build (tools/run shapeland-dev): a marker it left behind would make this one uncompressed.
        var marker = Path.Combine(Tool.Repo, "shapeland", "ShapeLand.Unity", "Library", "ShapeLand", "fast-web-build");
        if (File.Exists(marker))
            File.Delete(marker);
        if (Tool.RunTool("unity-batch", "build", "Web", web) != 0)
            throw Tool.Fail("The Unity web build failed.");
    }
    if (!File.Exists(Path.Combine(web, "index.html")))
        throw Tool.Fail($"No web build at {web}.");

    Tool.Quietly("dotnet", "run", "--project", Path.Combine(Tool.Repo, "shapeland", "src", "ShapeLand.ContentBuild"), "--", "build", "--release");
    if (Directory.Exists(output))
        Directory.Delete(output, recursive: true);
    foreach (var path in Directory.EnumerateFiles(web, "*", SearchOption.AllDirectories))
    {
        var copy = Path.Combine(output, Path.GetRelativePath(web, path));
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(path, copy);
    }
    Directory.CreateDirectory(Path.Combine(output, "StreamingAssets"));
    File.Copy(content, Path.Combine(output, "StreamingAssets", "content.bin"), overwrite: true);
    File.WriteAllText(
        Path.Combine(output, "vercel.json"),
        File.ReadAllText(Path.Combine(Tool.Repo, "shapeland", "deploy", "vercel.json")).Replace("{{SERVER_HOST}}", ServerHost)
    );
    File.WriteAllText(Path.Combine(output, "config.js"), $"window.shapelandLogin = \"https://{ServerHost}\";\n");

    // The page's name needs its own record: once server.shapeland exists, the domain's wildcard no longer covers
    // shapeland itself (it has a name under it).
    var records = Tool.Capture(Tool.Info("vercel", ["dns", "ls", Domain]))
        .Output.Split('\n')
        .Select(l => l.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
    if (!records.Any(f => f.Length >= 2 && f[1] == "shapeland"))
    {
        Tool.Output("vercel", "dns", "add", Domain, "shapeland", "CNAME", "cname.vercel-dns.com");
        Tool.Capture(Tool.Info("vercel", ["domains", "add", PageHost, VercelProject]), withErrors: true);
    }

    Console.WriteLine("== Publishing on Vercel");
    // The deployment's address is the last vercel.app URL in its output (the rest varies between CLI versions).
    var deploy = Tool.Capture(Tool.Info("vercel", ["deploy", "--prod", "--yes"], folder: output), withErrors: true).Output;
    var deployment =
        System.Text.RegularExpressions.Regex.Matches(deploy, @"https://[a-z0-9.-]*\.vercel\.app").LastOrDefault()?.Value
        ?? throw Tool.Fail("Vercel didn't report a deployment.");
    Tool.Output("vercel", "alias", "set", deployment, PageHost);
    Console.WriteLine($"Published: https://{PageHost}");
}
