namespace DotCraft.ProtocolGen;

public sealed record ContractIr(
    int FormatVersion,
    string ContractVersion,
    string ProtocolVersion,
    IReadOnlyList<ContractModule> Modules,
    IReadOnlyList<ContractType> Types,
    IReadOnlyList<ContractMethod> Methods);

public sealed record ContractModule(string Name, string Stability);

public enum ContractArtifactProfile
{
    Stable,
    Experimental
}

public sealed record ContractType(
    string Id,
    string Name,
    string Module,
    ContractTypeKind Kind,
    string SchemaPath,
    IReadOnlyList<ContractField> Fields,
    IReadOnlyList<ContractUnionVariant> Variants,
    bool AllowsAdditionalProperties,
    Type RuntimeType);

public enum ContractTypeKind
{
    Object,
    Enum,
    Union
}

public sealed record ContractField(
    string Name,
    ContractTypeRef Type,
    bool Required,
    bool Nullable,
    string? Constant = null);

public sealed record ContractUnionVariant(string Discriminator, string TypeId);

public sealed record ContractTypeRef(
    ContractTypeRefKind Kind,
    string DisplayName,
    string? TypeId = null,
    ContractTypeRef? ElementType = null,
    long? Minimum = null,
    long? Maximum = null);

public enum ContractTypeRefKind
{
    String,
    Boolean,
    Integer,
    Number,
    DateTime,
    AnyJson,
    Named,
    Array,
    Map
}

public sealed record ContractMethod(
    string Name,
    string Kind,
    string Direction,
    string ParamsType,
    string ResultType,
    string Module,
    string? Capability,
    string Scope,
    bool NotificationOptOut,
    IReadOnlyList<string> Errors,
    string Since,
    string SpecRef,
    string Stability);

public sealed class ProtocolGenerationException : Exception
{
    public ProtocolGenerationException(string code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
    }

    public string Code { get; }
}
