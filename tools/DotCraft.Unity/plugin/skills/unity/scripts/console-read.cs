var maxRows = (int?)Args["maxRows"] ?? 20;
var maxMessageLength = (int?)Args["maxMessageLength"] ?? 500;

var assembly = typeof(UnityEditor.Editor).Assembly;
var entriesType = assembly.GetType("UnityEditor.LogEntries");
var entryType = assembly.GetType("UnityEditor.LogEntry");
if (entriesType == null || entryType == null)
    return "Unity Console APIs were not found.";

var flags = System.Reflection.BindingFlags.Public
    | System.Reflection.BindingFlags.NonPublic
    | System.Reflection.BindingFlags.Static
    | System.Reflection.BindingFlags.Instance;
var getCount = entriesType.GetMethod("GetCount", flags);
var getCounts = entriesType.GetMethod("GetCountsByType", flags);
var getFilter = entriesType.GetMethod("GetFilteringText", flags);
var consoleFlags = entriesType.GetProperty("consoleFlags", flags);
var begin = entriesType.GetMethod("StartGettingEntries", flags);
var end = entriesType.GetMethod("EndGettingEntries", flags);
var getEntry = entriesType.GetMethod("GetEntryInternal", flags, null, new[] { typeof(int), entryType }, null);
var getRepeats = entriesType.GetMethod("GetEntryCount", flags, null, new[] { typeof(int) }, null);
if (getCount == null || getEntry == null)
    return "Required Unity Console APIs were not found.";

var fields = entryType.GetFields(flags).ToDictionary(field => field.Name, field => field);
var visibleRows = (int)getCount.Invoke(null, null);
var firstRow = System.Math.Max(0, visibleRows - System.Math.Max(1, maxRows));
object[] counts = { 0, 0, 0 };
getCounts?.Invoke(null, counts);

var output = new System.Text.StringBuilder();
output.AppendLine($"VisibleRows={visibleRows}; ReturnedRows={visibleRows - firstRow}");
output.AppendLine($"Counts errors={counts[0]}, warnings={counts[1]}, logs={counts[2]}");
output.AppendLine($"FilteringText={getFilter?.Invoke(null, null) ?? ""}");
output.AppendLine($"ConsoleFlags={consoleFlags?.GetValue(null, null) ?? ""}");

try
{
    begin?.Invoke(null, null);
    for (var row = firstRow; row < visibleRows; row++)
    {
        var entry = System.Activator.CreateInstance(entryType);
        if (getEntry.Invoke(null, new[] { (object)row, entry }) is bool ok && !ok)
            continue;

        var message = fields.TryGetValue("message", out var messageField) ? (string)(messageField.GetValue(entry) ?? "") : "";
        var file = fields.TryGetValue("file", out var fileField) ? (string)(fileField.GetValue(entry) ?? "") : "";
        var line = fields.TryGetValue("line", out var lineField) ? (int)lineField.GetValue(entry) : 0;
        var mode = fields.TryGetValue("mode", out var modeField) ? (int)modeField.GetValue(entry) : 0;
        var repeats = getRepeats == null ? 1 : (int)getRepeats.Invoke(null, new object[] { row });
        var severity = (mode & (1 | 2 | 16 | 64 | 256 | 2048 | 1048576 | 4194304)) != 0
            ? "error"
            : (mode & (128 | 512 | 4096)) != 0 ? "warning" : "log";

        message = message.Replace("\r", "\\r").Replace("\n", "\\n");
        if (message.Length > maxMessageLength)
            message = message.Substring(0, maxMessageLength) + "...";
        output.AppendLine($"[{row}] {severity} x{repeats} {file}:{line} {message}");
    }
}
finally
{
    end?.Invoke(null, null);
}

return output.ToString();
