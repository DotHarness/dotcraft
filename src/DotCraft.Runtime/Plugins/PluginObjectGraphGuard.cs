using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace DotCraft.Runtime;

/// <summary>Rejects object graphs that would retain a collectible plugin assembly in Host state.</summary>
internal static class PluginObjectGraphGuard
{
    private const int MaxDepth = 64;
    private const int MaxNodes = 20_000;
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> HostFields = new();

    public static void EnsureHostOwnedGraph(object? root, string description)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var nodes = 0;
        Visit(root, description, visited, ref nodes, depth: 0);
    }

    private static void Visit(
        object? value,
        string description,
        HashSet<object> visited,
        ref int nodes,
        int depth)
    {
        if (value is null)
            return;
        if (++nodes > MaxNodes || depth > MaxDepth)
            throw Rejected(description, "the returned object graph exceeds the containment limit");

        var type = value.GetType();
        if (ContainsCollectibleType(type))
            throw Rejected(description, "the returned object graph contains a plugin-defined type");

        switch (value)
        {
            case Type referencedType when ContainsCollectibleType(referencedType):
                throw Rejected(description, "the returned object graph contains a plugin-defined type token");
            case Assembly assembly when assembly.IsCollectible:
                throw Rejected(description, "the returned object graph contains a collectible assembly");
            case Module module when module.Assembly.IsCollectible:
                throw Rejected(description, "the returned object graph contains a collectible module");
            case MemberInfo member when MemberReferencesCollectibleAssembly(member):
                throw Rejected(description, "the returned object graph contains plugin reflection metadata");
        }

        if (IsLeaf(value, type))
            return;
        if (!type.IsValueType && !visited.Add(value))
            return;

        if (value is Delegate callback)
        {
            foreach (var handler in callback.GetInvocationList())
            {
                Visit(handler.Method, description, visited, ref nodes, depth + 1);
                Visit(handler.Target, description, visited, ref nodes, depth + 1);
            }
            return;
        }

        if (value is Array array)
        {
            foreach (var item in array)
                Visit(item, description, visited, ref nodes, depth + 1);
            return;
        }

        var fields = HostFields.GetOrAdd(type, static current => GetInstanceFields(current));
        foreach (var field in fields)
        {
            object? child;
            try
            {
                child = field.GetValue(value);
            }
            catch (Exception exception) when (exception is MemberAccessException or NotSupportedException)
            {
                throw Rejected(description, "the returned object graph could not be inspected");
            }
            Visit(child, description, visited, ref nodes, depth + 1);
        }
    }

    private static bool ContainsCollectibleType(Type type)
    {
        if (type.Assembly.IsCollectible)
            return true;
        if (type.HasElementType && type.GetElementType() is { } element && ContainsCollectibleType(element))
            return true;
        return type.IsGenericType && type.GetGenericArguments().Any(ContainsCollectibleType);
    }

    private static bool MemberReferencesCollectibleAssembly(MemberInfo member) =>
        member.Module.Assembly.IsCollectible
        || (member.DeclaringType is not null && ContainsCollectibleType(member.DeclaringType));

    private static bool IsLeaf(object value, Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || value is string
            or decimal
            or DateTime
            or DateTimeOffset
            or DateOnly
            or TimeOnly
            or TimeSpan
            or Guid
            or Uri
            or Version
            or JsonElement
            or JsonDocument
            or Type
            or Assembly
            or Module
            or MemberInfo
            or IntPtr
            or UIntPtr;

    private static FieldInfo[] GetInstanceFields(Type type)
    {
        var fields = new List<FieldInfo>();
        for (var current = type; current is not null; current = current.BaseType)
        {
            fields.AddRange(current.GetFields(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly));
        }
        return [.. fields];
    }

    private static NotSupportedException Rejected(string description, string reason) =>
        new($"A collectible plugin returned an unsafe {description}: {reason}.");
}
