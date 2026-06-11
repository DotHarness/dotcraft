namespace DotCraft.Sdk.Tools;

/// <summary>JSON Schema <c>minimum</c>.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class SchemaMinimumAttribute(double value) : Attribute
{
    public double Value => value;
}

/// <summary>JSON Schema <c>maximum</c>.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class SchemaMaximumAttribute(double value) : Attribute
{
    public double Value => value;
}

/// <summary>JSON Schema <c>pattern</c> (regular expression).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class SchemaPatternAttribute(string pattern) : Attribute
{
    public string Pattern => pattern;
}

/// <summary>JSON Schema array <c>maxItems</c>.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class SchemaMaxItemsAttribute(int value) : Attribute
{
    public int Value => value;
}

/// <summary>JSON Schema array <c>minItems</c>.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class SchemaMinItemsAttribute(int value) : Attribute
{
    public int Value => value;
}

/// <summary>
/// Marks a boolean property/parameter that must be <c>true</c> (a confirmation flag).
/// Emits <c>{ "type": "boolean", "enum": [true] }</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class SchemaConstTrueAttribute : Attribute
{
}

/// <summary>
/// Marks an object type that allows arbitrary extra properties, emitting
/// <c>"additionalProperties": true</c> (for free-form payloads).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SchemaAllowAdditionalPropertiesAttribute : Attribute
{
}
