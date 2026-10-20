using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Comet.Server;

/// <summary>
/// Logs to a file instead of the console when <c>Comet:LogFile</c> is set: for a server started by a tool (the Unity
/// editor) that can't keep reading its output, where a console nobody reads would fill up and stall the server.
/// </summary>
public static class FileLogging
{
    /// <summary>Sends the app's logs to <c>Comet:LogFile</c> only, if it's set (appending); otherwise changes nothing.</summary>
    public static WebApplicationBuilder AddCometLogFile(this WebApplicationBuilder builder)
    {
        var path = builder.Configuration["Comet:LogFile"];
        if (!string.IsNullOrEmpty(path))
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new FileLoggerProvider(Path.GetFullPath(path)));
        }

        return builder;
    }

    /// <summary>
    /// Writes a failure that stopped the server (before its logging, or around it) to <c>Comet:LogFile</c>, if the
    /// command line sets it, so a tool reading the file sees why the server stopped.
    /// </summary>
    public static void LogFailure(string[] args, Exception exception)
    {
        var path = new ConfigurationBuilder().AddCommandLine(args).Build()["Comet:LogFile"];
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            File.AppendAllText(Path.GetFullPath(path), $"{DateTime.Now:HH:mm:ss} Critical: the server stopped: {exception}\n");
        }
        catch (IOException)
        {
            // Nowhere left to say it but the console.
        }
    }

    private sealed class FileLoggerProvider(string path) : ILoggerProvider
    {
        private readonly StreamWriter _writer = Open(path);

        public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _writer);

        public void Dispose()
        {
            lock (_writer)
            {
                _writer.Dispose();
            }
        }

        // Others (the editor showing it) may read it while it's written.
        private static StreamWriter Open(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
        }
    }

    private sealed class FileLogger(string category, StreamWriter writer) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var line = $"{DateTime.Now:HH:mm:ss} {logLevel} {category}: {formatter(state, exception)}";
            lock (writer)
            {
                writer.WriteLine(exception is null ? line : $"{line}\n{exception}");
            }
        }
    }
}
