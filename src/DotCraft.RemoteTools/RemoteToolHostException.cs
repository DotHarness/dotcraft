namespace DotCraft.RemoteTools;

internal sealed class RemoteToolHostException : Exception
{
    public RemoteToolHostException(string code, string message, string? invocationId = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        InvocationId = invocationId;
    }

    public string Code { get; }
    public string? InvocationId { get; }
}
