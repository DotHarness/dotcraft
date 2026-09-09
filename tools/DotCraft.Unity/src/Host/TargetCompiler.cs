using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Security.Cryptography;

namespace DotCraft.Unity;

internal static class TargetCompiler
{
    public static string Compile(string sourcePath, IEnumerable<string> references, string cache, string prefix = "Attach_", string generation = "")
    {
        var paths = references.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        var source = Directory.Exists(sourcePath)
            ? string.Join("\n", Directory.GetFiles(sourcePath, "*.cs").Order().Select(File.ReadAllText))
            : File.ReadAllText(sourcePath);
        var identity = "CSharp9|Debug|" + generation + source + string.Join("\n", paths.Select(p => p + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))));
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
        Directory.CreateDirectory(cache);
        var output = Path.Combine(cache, prefix + hash + ".dll");
        if (File.Exists(output)) return output;
        var compilation = CSharpCompilation.Create(prefix + hash,
            Directory.Exists(sourcePath)
                ? Directory.GetFiles(sourcePath, "*.cs").Order().Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), new CSharpParseOptions(LanguageVersion.CSharp9), p))
                : new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp9), sourcePath) },
            paths.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        File.WriteAllBytes(output, stream.ToArray());
        return output;
    }
}
