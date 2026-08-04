namespace DotCraft.Protocol;

/// <summary>Declares the stable protocol module that owns a generated wire type.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum, Inherited = false)]
public sealed class ContractModuleAttribute(string name) : Attribute
{
    /// <summary>Stable module identifier used by manifests and schema paths.</summary>
    public string Name { get; } = name;
}

/// <summary>Direction of an AppServer JSON-RPC message.</summary>
public enum RpcDirection
{
    /// <summary>Client sends the message to AppServer.</summary>
    ClientToServer,

    /// <summary>AppServer sends the message to a client.</summary>
    ServerToClient
}

/// <summary>Stability classification retained by contract artifacts.</summary>
public enum RpcStability
{
    /// <summary>Covered by compatibility guarantees.</summary>
    Stable,

    /// <summary>May change before promotion.</summary>
    Experimental
}

/// <summary>Non-generic descriptor projection used by catalogs and artifact tooling.</summary>
public interface IRpcMethodDescriptor
{
    /// <summary>Wire method name.</summary>
    string Name { get; }

    /// <summary>Whether the descriptor is a request or notification.</summary>
    string Kind { get; }

    /// <summary>Message direction.</summary>
    RpcDirection Direction { get; }

    /// <summary>Wire params type.</summary>
    Type ParamsType { get; }

    /// <summary>Wire result type, or <see cref="RpcEmpty"/> for notifications.</summary>
    Type ResultType { get; }

    /// <summary>Owning protocol module.</summary>
    string Module { get; }

    /// <summary>Protocol version that introduced the method.</summary>
    string Since { get; }

    /// <summary>Owning specification reference.</summary>
    string SpecRef { get; }

    /// <summary>Stability classification.</summary>
    RpcStability Stability { get; }

    /// <summary>Optional capability gate.</summary>
    string? Capability { get; }

    /// <summary>Connection, thread, or workspace scope.</summary>
    string Scope { get; }

    /// <summary>Whether normal notification broadcasts honor connection opt-out.</summary>
    bool NotificationOptOut { get; }

    /// <summary>Stable error codes associated with the method.</summary>
    IReadOnlyList<string> Errors { get; }
}

/// <summary>Typed request descriptor.</summary>
public sealed record RpcRequest<TParams, TResult> : IRpcMethodDescriptor
{
    /// <summary>Creates a request descriptor.</summary>
    public RpcRequest(
        string name,
        RpcDirection direction,
        string since,
        string specRef,
        string module = "core",
        RpcStability stability = RpcStability.Stable,
        string? capability = null,
        string scope = "connection",
        IReadOnlyList<string>? errors = null)
    {
        Name = name;
        Direction = direction;
        Since = since;
        SpecRef = specRef;
        Module = module;
        Stability = stability;
        Capability = capability;
        Scope = scope;
        Errors = errors ?? [];
    }

    /// <inheritdoc />
    public string Name { get; }
    /// <inheritdoc />
    public string Kind => "request";
    /// <inheritdoc />
    public RpcDirection Direction { get; }
    /// <inheritdoc />
    public Type ParamsType => typeof(TParams);
    /// <inheritdoc />
    public Type ResultType => typeof(TResult);
    /// <inheritdoc />
    public string Module { get; }
    /// <inheritdoc />
    public string Since { get; }
    /// <inheritdoc />
    public string SpecRef { get; }
    /// <inheritdoc />
    public RpcStability Stability { get; }
    /// <inheritdoc />
    public string? Capability { get; }
    /// <inheritdoc />
    public string Scope { get; }
    /// <inheritdoc />
    public bool NotificationOptOut => false;
    /// <inheritdoc />
    public IReadOnlyList<string> Errors { get; }
}

/// <summary>Typed notification descriptor.</summary>
public sealed record RpcNotification<TParams> : IRpcMethodDescriptor
{
    /// <summary>Creates a notification descriptor.</summary>
    public RpcNotification(
        string name,
        RpcDirection direction,
        string since,
        string specRef,
        string module = "core",
        RpcStability stability = RpcStability.Stable,
        string? capability = null,
        string scope = "connection",
        bool notificationOptOut = false)
    {
        Name = name;
        Direction = direction;
        Since = since;
        SpecRef = specRef;
        Module = module;
        Stability = stability;
        Capability = capability;
        Scope = scope;
        NotificationOptOut = notificationOptOut;
    }

    /// <inheritdoc />
    public string Name { get; }
    /// <inheritdoc />
    public string Kind => "notification";
    /// <inheritdoc />
    public RpcDirection Direction { get; }
    /// <inheritdoc />
    public Type ParamsType => typeof(TParams);
    /// <inheritdoc />
    public Type ResultType => typeof(RpcEmpty);
    /// <inheritdoc />
    public string Module { get; }
    /// <inheritdoc />
    public string Since { get; }
    /// <inheritdoc />
    public string SpecRef { get; }
    /// <inheritdoc />
    public RpcStability Stability { get; }
    /// <inheritdoc />
    public string? Capability { get; }
    /// <inheritdoc />
    public string Scope { get; }
    /// <inheritdoc />
    public bool NotificationOptOut { get; }
    /// <inheritdoc />
    public IReadOnlyList<string> Errors => [];
}

/// <summary>Shared empty JSON object for methods without params or result fields.</summary>
public sealed class RpcEmpty
{
}
