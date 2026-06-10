using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Cron;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol.AppServer;


// ───── command/* (spec Section 19) ─────

public sealed class CommandListParams
{
    /// <summary>
    /// Deprecated compatibility field. The server ignores this value and always
    /// returns English fallback text plus stable message keys.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Language { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IncludeBuiltins { get; set; }
}

public sealed class CommandListResult
{
    public List<CommandInfoWire> Commands { get; set; } = [];
}

public sealed class CommandInfoWire
{
    public string Name { get; set; } = string.Empty;

    public string[] Aliases { get; set; } = [];

    public string DescriptionKey { get; set; } = string.Empty;

    public string FallbackDescription { get; set; } = string.Empty;

    /// <summary>
    /// Compatibility alias for <see cref="FallbackDescription"/>.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "builtin";

    public bool RequiresAdmin { get; set; }
}

public sealed class CommandExecuteParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Arguments { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SenderContext? Sender { get; set; }
}

public sealed class CommandExecuteResult
{
    public bool Handled { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    public bool IsMarkdown { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpandedPrompt { get; set; }

    /// <summary>
    /// True when command handling reset the conversation and switched to a new thread.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SessionReset { get; set; }

    /// <summary>
    /// Fresh thread metadata returned by reset-style commands (for example <c>/new</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionWireThread? Thread { get; set; }

    /// <summary>
    /// Thread ids archived as part of reset-style commands.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ArchivedThreadIds { get; set; }

    /// <summary>
    /// Whether the newly created thread is lazily materialized on disk.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CreatedLazily { get; set; }
}
