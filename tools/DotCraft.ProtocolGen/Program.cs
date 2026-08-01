using System.Text.Json;
using DotCraft.ProtocolGen;

return await RunAsync(args);

static Task<int> RunAsync(string[] arguments)
{
    try
    {
        var repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);
        if (arguments.Length == 0)
            return Task.FromResult(Usage());
        var profile = ReadProfile(arguments);

        switch (arguments[0])
        {
            case "generate":
                ProtocolArtifactGenerator.Generate(repositoryRoot, profile);
                Console.WriteLine("Generated AppServer contract artifacts.");
                return Task.FromResult(0);
            case "validate":
                ProtocolArtifactGenerator.Validate(repositoryRoot, ReadOptions(arguments, "--module"), profile);
                Console.WriteLine("AppServer contract graph and artifacts are valid.");
                return Task.FromResult(0);
            case "check":
                var drift = ProtocolArtifactGenerator.Check(repositoryRoot, profile);
                if (drift.Count == 0)
                {
                    Console.WriteLine("AppServer contract artifacts are up to date.");
                    return Task.FromResult(0);
                }
                foreach (var entry in drift)
                    Console.Error.WriteLine(entry);
                return Task.FromResult(1);
            case "diff":
                var baselineValue = ReadOption(arguments, "--against") ?? throw new ArgumentException("diff requires --against <contract-package-or-manifest>.");
                var baseline = Path.GetFullPath(baselineValue);
                var previousManifestPath = Directory.Exists(baseline) ? Path.Combine(baseline, "appserver.manifest.json") : baseline;
                var current = ProtocolArtifactGenerator.Build(repositoryRoot, profile)["appserver.manifest.json"];
                var changes = ContractPackageDiffer.Compare(File.ReadAllText(previousManifestPath), current);
                Console.WriteLine(JsonSerializer.Serialize(changes, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
                return Task.FromResult(changes.Any(static change => change.Classification == ContractChangeClassification.Breaking) ? 2 : 0);
            default:
                return Task.FromResult(Usage());
        }
    }
    catch (Exception exception) when (exception is ProtocolGenerationException or ArgumentException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine(exception.Message);
        return Task.FromResult(1);
    }
}

static string FindRepositoryRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "dotcraft.sln")))
            return directory.FullName;
        directory = directory.Parent;
    }
    throw new ArgumentException("Could not locate the repository root.");
}

static string? ReadOption(IReadOnlyList<string> arguments, string name)
{
    for (var index = 0; index < arguments.Count - 1; index++)
    {
        if (arguments[index] == name)
            return arguments[index + 1];
    }
    return null;
}

static IReadOnlyList<string> ReadOptions(IReadOnlyList<string> arguments, string name)
{
    var values = new List<string>();
    for (var index = 0; index < arguments.Count - 1; index++)
    {
        if (arguments[index] == name)
            values.Add(arguments[index + 1]);
    }
    return values;
}

static ContractArtifactProfile ReadProfile(IReadOnlyList<string> arguments)
{
    var value = ReadOption(arguments, "--profile") ?? "stable";
    return value switch
    {
        "stable" => ContractArtifactProfile.Stable,
        "experimental" => ContractArtifactProfile.Experimental,
        _ => throw new ArgumentException("--profile must be 'stable' or 'experimental'.")
    };
}

static int Usage()
{
    Console.Error.WriteLine("Usage: DotCraft.ProtocolGen <generate|validate [--module <name>]|check|diff --against <path>> [--profile stable|experimental]");
    return 1;
}
