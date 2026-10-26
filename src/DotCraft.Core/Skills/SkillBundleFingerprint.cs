using System.Security.Cryptography;
using System.Text;

namespace DotCraft.Skills;

/// <summary>
/// SHA-256 over the relative path and contents of every ordinary file in a Skill bundle, sorted by
/// relative path. Dot-prefixed files, including deployment and install markers, are excluded.
/// </summary>
public static class SkillBundleFingerprint
{
    public static string Compute(string skillFilePath)
    {
        var skillDir = Path.GetDirectoryName(skillFilePath)
            ?? throw new InvalidOperationException("Skill file path has no parent directory.");
        var root = Path.GetFullPath(skillDir);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith('.'))
            .Select(Path.GetFullPath)
            .OrderBy(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.Ordinal)
            .ToArray();

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            hasher.AppendData(Encoding.UTF8.GetBytes(relative));
            hasher.AppendData([0]);
            hasher.AppendData(File.ReadAllBytes(file));
            hasher.AppendData([0]);
        }

        return "sha256:" + Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }
}
