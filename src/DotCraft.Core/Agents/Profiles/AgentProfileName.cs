using System.Security.Cryptography;
using System.Text;

namespace DotCraft.Agents;

/// <summary>The canonical name shared by profile lookup, authoring, and visual identity.</summary>
public static class AgentProfileName
{
    /// <summary>Removes surrounding whitespace and normalizes Unicode without changing case.</summary>
    public static string Canonicalize(string value) => value.Trim().Normalize(NormalizationForm.FormC);

    /// <summary>Checks a name independently of platform filename restrictions.</summary>
    public static bool IsValid(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            var name = Canonicalize(value);
            return name.EnumerateRunes().Count() <= 240 && !value.Any(char.IsControl);
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>Returns the canonical name or a profile validation error.</summary>
    public static string Normalize(string value)
    {
        if (!IsValid(value))
            throw new AgentProfileException(AgentProfileErrorKind.ValidationFailed,
                "Use 1–240 Unicode characters without control characters for the profile name.");
        return Canonicalize(value);
    }

    /// <summary>Returns an opaque, portable filename; the document remains the identity authority.</summary>
    public static string FileName(string name) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(name)))).ToLowerInvariant() + ".md";
}
