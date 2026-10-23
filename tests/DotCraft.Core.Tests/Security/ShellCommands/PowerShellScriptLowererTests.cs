using System.Reflection;
using System.Text.Json;
using DotCraft.Security.ShellCommands;
using Xunit;

namespace DotCraft.Tests.Security.ShellCommands;

public sealed class PowerShellScriptLowererTests
{
    private static readonly IReadOnlyList<LoweringCase> Cases = LoadCases();

    [Fact]
    public void LowersEveryFixtureScript()
    {
        Assert.NotEmpty(Cases);
        var failures = new List<string>();
        foreach (var fixture in Cases)
        {
            try
            {
                AssertLowering(fixture);
            }
            catch (Exception exception) when (exception is Xunit.Sdk.XunitException)
            {
                failures.Add($"script: {JsonSerializer.Serialize(fixture.Script)}{Environment.NewLine}{exception.Message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine + Environment.NewLine, failures));
    }

    private static void AssertLowering(LoweringCase fixture)
    {
        var lowered = new PowerShellScriptLowerer().Lower(fixture.Script);

        Assert.Equal(ShellFamily.PowerShell, lowered.Family);

        foreach (var expected in fixture.Literals ?? [])
        {
            Assert.Contains(lowered.LiteralCommands, command => command.SequenceEqual(expected));
        }

        if (fixture.Expected is null)
        {
            Assert.True(lowered.PlainCommands is null, "lowered plainly but was expected to be opaque.");
            Assert.False(string.IsNullOrWhiteSpace(lowered.PlainRejectReason), "opaque without a reason.");
            return;
        }

        Assert.True(lowered.IsPlain, $"was opaque: {lowered.PlainRejectReason}");
        Assert.Null(lowered.PlainRejectReason);
        Assert.Equal(fixture.Expected, Flatten(lowered.PlainCommands!));
        Assert.Equal(fixture.Expected, Flatten(lowered.LiteralCommands));
    }

    [Fact]
    public void LowersOnEveryOperatingSystem()
    {
        var lowered = new PowerShellScriptLowerer().Lower("Get-ChildItem -Path .");

        Assert.True(lowered.IsPlain, lowered.PlainRejectReason);
        Assert.Equal([["Get-ChildItem", "-Path", "."]], Flatten(lowered.PlainCommands!));
    }

    private static string[][] Flatten(IReadOnlyList<IReadOnlyList<string>> commands) =>
        [.. commands.Select(command => command.ToArray())];

    private static IReadOnlyList<LoweringCase> LoadCases()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("DotCraft.Tests.PowerShellLowering.json")
            ?? throw new InvalidOperationException("Embedded PowerShell lowering fixture was not found.");

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var cases = JsonSerializer.Deserialize<List<LoweringCase>>(stream, options)
            ?? throw new InvalidOperationException("PowerShell lowering fixture is empty.");

        // Null fields would lower a null script and let every case pass vacuously.
        if (cases.Any(one => one.Script is null))
            throw new InvalidOperationException("PowerShell lowering fixture did not bind to its cases.");

        return cases;
    }

    private sealed record LoweringCase(string Script, string[][]? Expected, string[][]? Literals, string? Note);
}
