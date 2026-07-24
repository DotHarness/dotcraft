using System.Text;
using System.Text.RegularExpressions;
using DotCraft.Sdk.Tools;

namespace DotCraft.Sdk.AppServer;

/// <summary>
/// Projects attribute-authored dynamic tool descriptors into the Runtime Dynamic Tool
/// declaration union accepted by AppServer thread start and resume requests.
/// </summary>
public static partial class RuntimeDynamicToolDeclarationBuilder
{
    /// <summary>
    /// Builds namespaced Runtime Dynamic Tool declarations from the selected descriptors.
    /// </summary>
    /// <param name="descriptors">Descriptors to expose on the thread.</param>
    /// <param name="namespaceDescriptions">
    /// Model-visible description for every namespace present in <paramref name="descriptors"/>.
    /// </param>
    /// <param name="approvalResolver">
    /// Optional callback that supplies AppServer approval metadata for each function.
    /// </param>
    public static IReadOnlyList<RuntimeDynamicToolDeclaration> Build(
        IEnumerable<DynamicToolDescriptor> descriptors,
        IReadOnlyDictionary<string, string> namespaceDescriptions,
        Func<DynamicToolDescriptor, ToolApprovalDescriptor?>? approvalResolver = null)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(namespaceDescriptions);

        DynamicToolDescriptor[] selected = descriptors
            .OrderBy(descriptor => descriptor.Order)
            .ThenBy(descriptor => descriptor.QualifiedName, StringComparer.Ordinal)
            .ToArray();

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (DynamicToolDescriptor descriptor in selected)
        {
            ValidateDescriptor(descriptor);
            if (!identities.Add(descriptor.QualifiedName))
            {
                throw new InvalidOperationException(
                    $"Duplicate Runtime Dynamic Tool identity '{descriptor.QualifiedName}'.");
            }
        }

        return selected
            .GroupBy(descriptor => descriptor.Namespace, StringComparer.Ordinal)
            .Select(group =>
            {
                if (!namespaceDescriptions.TryGetValue(group.Key, out string? description) ||
                    string.IsNullOrWhiteSpace(description))
                {
                    throw new InvalidOperationException(
                        $"Runtime Dynamic Tool namespace '{group.Key}' requires a non-empty description.");
                }

                RuntimeDynamicToolDeclaration[] functions = group
                    .Select(descriptor => (RuntimeDynamicToolDeclaration)new RuntimeDynamicToolFunction(
                        descriptor.LocalName,
                        descriptor.Description,
                        descriptor.InputSchema,
                        descriptor.DeferLoading,
                        approvalResolver?.Invoke(descriptor)))
                    .ToArray();

                return new
                {
                    FirstOrder = group.Min(descriptor => descriptor.Order),
                    Declaration = (RuntimeDynamicToolDeclaration)new RuntimeDynamicToolNamespace(
                        group.Key,
                        description,
                        functions),
                };
            })
            .OrderBy(entry => entry.FirstOrder)
            .ThenBy(entry => entry.Declaration.Name, StringComparer.Ordinal)
            .Select(entry => entry.Declaration)
            .ToArray();
    }

    private static void ValidateDescriptor(DynamicToolDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Namespace))
        {
            throw new InvalidOperationException(
                $"Attribute-authored Runtime Dynamic Tool '{descriptor.QualifiedName}' requires a namespace.");
        }

        ValidateIdentifier(descriptor.Namespace, "namespace");
        ValidateIdentifier(descriptor.LocalName, "function");

        if (string.IsNullOrWhiteSpace(descriptor.Description))
        {
            throw new InvalidOperationException(
                $"Runtime Dynamic Tool '{descriptor.QualifiedName}' requires a non-empty description.");
        }

        string flatName = $"{descriptor.Namespace}__{descriptor.LocalName}";
        if (Encoding.ASCII.GetByteCount(flatName) > 64)
        {
            throw new InvalidOperationException(
                $"Runtime Dynamic Tool flat name '{flatName}' exceeds 64 ASCII bytes.");
        }
    }

    private static void ValidateIdentifier(string value, string kind)
    {
        if (!IdentifierPattern().IsMatch(value))
        {
            throw new InvalidOperationException(
                $"Runtime Dynamic Tool {kind} name '{value}' must match ^[A-Za-z0-9_]+$.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
