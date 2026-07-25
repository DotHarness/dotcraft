namespace DotCraft.Agents;

/// <summary>
/// Controls whether <see cref="StreamingFunctionInvokingChatClient"/> emits
/// <see cref="ToolCallArgumentsDeltaContent"/> previews for the decorated tool method.
/// </summary>
/// <remarks>
/// <para>
/// By default every tool streams argument deltas. Apply this attribute with
/// <see cref="Enabled"/> set to <see langword="false"/> on a tool method to opt it out
/// (for example, when the tool's arguments are trivial or when streaming the raw
/// argument JSON would not meaningfully improve UX).
/// </para>
/// <para>
/// The attribute is read via <see cref="Microsoft.Extensions.AI.AIFunction.UnderlyingMethod"/>,
/// so it works for tools created with
/// <c>Microsoft.Extensions.AI.AIFunctionFactory.Create(Delegate)</c> and for tools wrapped
/// through <c>DelegatingAIFunction</c> (e.g. hook wrappers, result-size limiters).
/// Tools without an <c>UnderlyingMethod</c> (e.g. MCP tools) are always streamable.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StreamArgumentsAttribute(bool enabled = true) : Attribute
{
    /// <summary>
    /// When <see langword="true"/> (default) the tool emits argument deltas; when
    /// <see langword="false"/> the client suppresses deltas and clients only see the
    /// final <see cref="Microsoft.Extensions.AI.FunctionCallContent"/> on completion.
    /// </summary>
    public bool Enabled { get; } = enabled;
}
