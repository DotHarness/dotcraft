using System.Text.Json.Nodes;

namespace DotCraft.CodeMode;

internal static class CodeModeProtocol
{
    public const string WorkerKind = "code-mode";
    public const string WorkerScope = "worker";
    public const string Ready = "ready";
    public const string CellStart = "cell.start";
    public const string CellCancel = "cell.cancel";
    public const string CallRequest = "call.request";
    public const string CallResult = "call.result";
    public const string CellOutput = "cell.output";
    public const string CellDone = "cell.done";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string TimedOut = "timedOut";
    public const string TimeoutReason = "timeout";

    public static string ErrorEnvelope(string error) =>
        new JsonObject { ["ok"] = false, ["error"] = error }.ToJsonString();
}
