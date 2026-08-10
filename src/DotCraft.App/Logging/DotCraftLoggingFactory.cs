using System.Globalization;
using DotCraft.Configuration;
using Microsoft.Extensions.Logging;
using ZLogger;
using ZLogger.Providers;

namespace DotCraft.Logging;

internal static class DotCraftLoggingFactory
{
    internal const int RollingSizeKilobytes = 64 * 1024;
    internal const int BackgroundBufferCapacity = 10_000;

    internal static ILoggerFactory CreateWorkspace(
        AppConfig.LoggingConfig config,
        string craftPath,
        bool reservesStdout)
        => Create(config, craftPath, "dotcraft", reservesStdout);

    internal static ILoggerFactory CreateHub(
        AppConfig.LoggingConfig config,
        string craftHomePath)
        => Create(config, craftHomePath, "dotcraft-hub", reservesStdout: false);

    private static ILoggerFactory Create(
        AppConfig.LoggingConfig config,
        string craftPath,
        string filePrefix,
        bool reservesStdout)
    {
        var minLevel = Enum.TryParse<LogLevel>(config.MinLevel, ignoreCase: true, out var configuredLevel)
            ? configuredLevel
            : LogLevel.Information;
        var fallback = new LoggingFallbackReporter();

        try
        {
            var logsDirectory = Path.GetFullPath(Path.Combine(craftPath, config.Directory));
            if (config.Enabled)
            {
                Directory.CreateDirectory(logsDirectory);
                LogRetentionCleaner.Purge(logsDirectory, filePrefix, config.RetentionDays);
            }

            var factory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(minLevel);
                // Keep routine framework connection and host lifecycle noise out of the
                // operational log. DotCraft emits its own host lifecycle events.
                builder.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
                builder.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);

                if (config.Enabled)
                {
                    builder.AddZLoggerRollingFile(options =>
                    {
                        options.FilePathSelector = (timestamp, sequenceNumber) =>
                        {
                            return Path.Combine(
                                logsDirectory,
                                $"{filePrefix}-{timestamp.ToLocalTime():yyyy-MM-dd}_{sequenceNumber:000}.log");
                        };
                        options.RollingInterval = RollingInterval.Day;
                        options.RollingSizeKB = RollingSizeKilobytes;
                        // Workspace and Hub locks provide one long-lived owner per log set.
                        // Keep the file exclusive so an accidental competing process cannot
                        // interleave writes or race rolling-file sequence selection.
                        options.FileShared = false;
                        options.IncludeScopes = true;
                        options.FullMode = BackgroundBufferFullMode.Block;
                        options.BackgroundBufferCapacity = BackgroundBufferCapacity;
                        options.InternalErrorLogger = fallback.Report;
                        options.UsePlainTextFormatter(formatter =>
                        {
                            formatter.SetPrefixFormatter(
                                $"{0:local-longdate} [{1:short}] [pid:{2}] ",
                                (in MessageTemplate template, in LogInfo info) =>
                                    template.Format(info.Timestamp, info.LogLevel, Environment.ProcessId));
                            formatter.SetSuffixFormatter(
                                $" ({0}){1}",
                                (in MessageTemplate template, in LogInfo info) =>
                                    template.Format(info.Category, FormatScopes(info.ScopeState)));
                        });
                    });
                }

                if (config.Console)
                {
                    builder.AddZLoggerConsole(options =>
                    {
                        if (reservesStdout)
                            options.LogToStandardErrorThreshold = LogLevel.Trace;
                        options.IncludeScopes = true;
                        options.InternalErrorLogger = fallback.Report;
                    });
                }
            });
            return new ResilientLoggerFactory(factory, fallback);
        }
        catch (Exception ex)
        {
            fallback.Report(ex);
            return new ResilientLoggerFactory(
                LoggerFactory.Create(builder => builder.SetMinimumLevel(minLevel)),
                fallback);
        }
    }

    private static string FormatScopes(LogScopeState? scopeState)
    {
        if (scopeState is null || scopeState.IsEmpty)
            return string.Empty;

        return " [scope:" + string.Join(
            ", ",
            scopeState.Properties.ToArray().Select(static property => $"{property.Key}={property.Value}")) + "]";
    }

    private sealed class ResilientLoggerFactory(
        ILoggerFactory inner,
        LoggingFallbackReporter fallback) : ILoggerFactory
    {
        private int _disposed;

        public ILogger CreateLogger(string categoryName) => inner.CreateLogger(categoryName);

        public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                inner.Dispose();
            }
            catch (Exception ex)
            {
                fallback.Report(ex);
            }
        }
    }

    private sealed class LoggingFallbackReporter
    {
        private int _reported;

        internal void Report(Exception exception)
        {
            if (Interlocked.Exchange(ref _reported, 1) != 0)
                return;

            try
            {
                Console.Error.WriteLine($"[Logging] Persistent logging failed: {exception}");
            }
            catch
            {
                // Logging failure must never escape into application work.
            }
        }
    }
}

internal static class LogRetentionCleaner
{
    internal static void Purge(string logsDirectory, string filePrefix, int retentionDays)
    {
        if (retentionDays <= 0)
            return;

        try
        {
            var cutoff = DateTime.Today.AddDays(-retentionDays);
            var dateOffset = filePrefix.Length + 1;
            foreach (var path in Directory.EnumerateFiles(logsDirectory, $"{filePrefix}-*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (name.Length < dateOffset + 10)
                    continue;

                var dateText = name.Substring(dateOffset, 10);
                if (DateTime.TryParseExact(
                        dateText,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var fileDate)
                    && fileDate < cutoff)
                {
                    File.Delete(path);
                }
            }
        }
        catch
        {
            // Retention cleanup is best effort and must not prevent logging startup.
        }
    }
}
