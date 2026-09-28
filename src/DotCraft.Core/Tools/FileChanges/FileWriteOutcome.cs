using System.Text.Json;
using DotCraft.Sessions;

namespace DotCraft.Tools;

internal sealed class FileWriteOutcome
{
    private string _state = "notApplied";

    public void BeginWrite() => _state = "unknown";

    public ToolExecutionResult Fail(string message, string code)
    {
        if (_state == "unknown")
            TurnDiffTrackerScope.Current?.Invalidate();
        return new(false, message, FileChangeStructuredContent.Outcome(_state), error: new ToolError(code, message));
    }

    public ToolExecutionResult Fail(Exception exception, string operation)
    {
        var error = ToolFailure.FromException(exception, operation);
        return Fail($"{operation}: {error.Message}", error.Code);
    }

    public ToolExecutionResult Complete(FileChangeRecord change, string message,
        Func<FileChangeRecord, JsonElement> report)
    {
        _state = "applied";
        TurnDiffTrackerScope.Current?.Track(change);
        try
        {
            return ToolExecutionResult.Succeeded(message, report(change));
        }
        catch (Exception exception)
        {
            ToolInvocationDiagnostics.Report(exception, "File change reporting");
            const string warning = "The file was written, but its change report is unavailable. Do not repeat the write.";
            return ToolExecutionResult.Succeeded(message + "\nWarning: " + warning,
                FileChangeStructuredContent.Outcome(_state, warning));
        }
    }
}
