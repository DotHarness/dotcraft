namespace DotCraft.Diagnostics;

/// <summary>
/// Global debug mode state management service.
/// Controls whether tool call arguments and results should be displayed in full or truncated.
/// </summary>
public static class DebugModeService
{
    private static bool _isEnabled;

    /// <summary>
    /// Initialize debug mode state (should be called once during application startup).
    /// </summary>
    /// <param name="enabled">Initial debug mode state from configuration.</param>
    public static void Initialize(bool enabled)
    {
        _isEnabled = enabled;
    }

    /// <summary>
    /// Toggle debug mode state (on/off) and return the new state.
    /// </summary>
    /// <returns>The new debug mode state after toggling.</returns>
    public static bool Toggle()
    {
        _isEnabled = !_isEnabled;
        return _isEnabled;
    }

    /// <summary>
    /// Get current debug mode state.
    /// </summary>
    /// <returns>True if debug mode is enabled, false otherwise.</returns>
    public static bool IsEnabled()
    {
        return _isEnabled;
    }

    /// <summary>
    /// Set debug mode state.
    /// </summary>
    /// <param name="enabled">True to enable debug mode, false to disable.</param>
    public static void SetEnabled(bool enabled)
    {
        _isEnabled = enabled;
    }

    /// <summary>
    /// Log a message to stderr if debug mode is enabled.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void LogIfEnabled(string message)
    {
        if (_isEnabled)
        {
            Console.Error.WriteLine($"[DEBUG] {message}");
        }
    }
}
