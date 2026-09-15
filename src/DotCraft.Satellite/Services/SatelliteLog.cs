using System.Text;

namespace DotCraft.Satellite.Services;

internal sealed class SatelliteLog
{
    private readonly object _gate = new();
    private readonly string _directory;

    public SatelliteLog(string directory) => _directory = directory;

    public static SatelliteLog CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".craft",
        "logs"));

    public void Information(string eventName, string message) =>
        Write("INF", eventName, message, exception: null);

    public void Warning(string eventName, string message, Exception? exception = null) =>
        Write("WRN", eventName, message, exception);

    public void Error(string eventName, string message, Exception? exception = null) =>
        Write("ERR", eventName, message, exception);

    private void Write(string level, string eventName, string message, Exception? exception)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, $"dotcraft-satellite-{DateTime.UtcNow:yyyy-MM-dd}_000.log");
                var line = $"{DateTimeOffset.UtcNow:O} [{level}] {SingleLine(eventName)} {SingleLine(message)}";
                if (exception is not null)
                    line += $" exception={SingleLine(exception.GetType().FullName)}: {SingleLine(exception.Message)}";
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Diagnostics must never become a new failure path for the satellite process.
        }
    }

    private static string SingleLine(string? value) =>
        (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
}
