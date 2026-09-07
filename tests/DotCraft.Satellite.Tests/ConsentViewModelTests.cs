using System.Globalization;
using DotCraft.RemoteTools;
using DotCraft.Satellite.Localization;
using DotCraft.Satellite.Services;
using DotCraft.Satellite.ViewModels;
using Xunit;

namespace DotCraft.Satellite.Tests;

public sealed class ConsentViewModelTests : IDisposable
{
    private static readonly SatelliteStrings Strings =
        SatelliteStrings.For("en", CultureInfo.InvariantCulture);

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "DotCraftSatelliteConsent_" + Guid.NewGuid().ToString("N"));

    public ConsentViewModelTests() => Directory.CreateDirectory(_folder);

    [Fact]
    public void NewInvitation_SuggestsUncreatedFolderAndAllowsPreferredMode()
    {
        var viewModel = NewViewModel();

        Assert.True(Path.IsPathFullyQualified(viewModel.FolderPath));
        Assert.False(Directory.Exists(viewModel.FolderPath));
        Assert.False(viewModel.HasWarning);
        Assert.True(viewModel.CanAllow);

        viewModel.FolderPath = _folder;

        Assert.True(viewModel.CanAllow);
        Assert.False(viewModel.HasWarning);
    }

    [Fact]
    public async Task FullAccess_RequiresAcknowledgementAndPreservesFolderWhenSwitchingBack()
    {
        RemoteToolJoinDecision? decision = null;
        var viewModel = NewViewModel(accept: (value, _) =>
        {
            decision = value;
            return Task.CompletedTask;
        });
        viewModel.FolderPath = _folder;

        viewModel.FullAccess = true;
        Assert.False(viewModel.WorkspacePreferred);
        Assert.False(viewModel.CanAllow);
        await viewModel.AllowCommand.ExecuteAsync(null);
        Assert.Null(decision);

        viewModel.Acknowledged = true;
        await viewModel.AllowCommand.ExecuteAsync(null);
        Assert.Equal(RemoteToolAuthorization.FullAccess, decision?.AuthorizationMode);

        viewModel.FullAccess = false;
        Assert.True(viewModel.WorkspacePreferred);
        Assert.Equal(_folder, viewModel.FolderPath);
        Assert.True(viewModel.CanAllow);
        viewModel.FullAccess = true;
        Assert.False(viewModel.Acknowledged);
        Assert.False(viewModel.CanAllow);
    }

    [Fact]
    public void CanAllow_RequiresAnExistingFolderThatIsNotAWholeDriveOrProfile()
    {
        var viewModel = NewViewModel();

        viewModel.FolderPath = Path.Combine(_folder, "missing");
        Assert.False(viewModel.CanAllow);
        Assert.Equal(Strings["consent.warningFolder"], viewModel.Warning);

        viewModel.FolderPath = "relative\\path";
        Assert.False(viewModel.CanAllow);

        viewModel.FolderPath = Path.GetPathRoot(_folder)!;
        Assert.False(viewModel.CanAllow);
        Assert.Equal(Strings["consent.warningRoot"], viewModel.Warning);

        viewModel.FolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(viewModel.CanAllow);
        Assert.Equal(Strings["consent.warningRoot"], viewModel.Warning);
    }

    [Fact]
    public async Task Allow_PairsOnceWithTheChosenFolder()
    {
        var accepted = new List<string>();
        var picked = Path.Combine(_folder, "chosen");
        Directory.CreateDirectory(picked);
        var viewModel = NewViewModel(
            picker: new StubFolderPicker(picked),
            accept: (folder, _) =>
            {
                accepted.Add(folder.WorkspacePath);
                return Task.CompletedTask;
            });
        var finished = new List<bool>();
        viewModel.Finished += (_, result) => finished.Add(result);

        await viewModel.ChangeFolderCommand.ExecuteAsync(null);
        await viewModel.AllowCommand.ExecuteAsync(null);
        await viewModel.AllowCommand.ExecuteAsync(null);

        Assert.Equal([picked, picked], accepted);
        Assert.Equal([true, true], finished);
    }

    [Fact]
    public async Task Allow_ReportsAFailedPairing_WithoutFinishing()
    {
        var viewModel = NewViewModel(
            accept: (_, _) => throw new InvalidOperationException("the Hub refused"));
        viewModel.FolderPath = _folder;
        var finished = false;
        viewModel.Finished += (_, _) => finished = true;

        await viewModel.AllowCommand.ExecuteAsync(null);

        Assert.False(finished);
        Assert.Contains("the Hub refused", viewModel.Warning, StringComparison.Ordinal);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ExpiredInvitation_BlocksAllowAndSaysWhy()
    {
        var accepted = 0;
        var viewModel = NewViewModel(
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            accept: (_, _) =>
            {
                accepted++;
                return Task.CompletedTask;
            });

        await viewModel.AllowCommand.ExecuteAsync(null);

        Assert.False(viewModel.CanAllow);
        Assert.Equal(0, accepted);
        Assert.Equal(Strings["consent.warningExpired"], viewModel.Warning);
    }

    [Fact]
    public void AttackerText_IsStrippedOfControlCharactersAndCapped()
    {
        var viewModel = NewViewModel(
            inviter: "A\u0007nn\r\n",
            purpose: new string('x', 400) + "\0");

        Assert.Equal("Ann", viewModel.InviterName);
        Assert.Equal(280, viewModel.Purpose.Length);
        Assert.DoesNotContain(viewModel.Purpose, character => char.IsControl(character));
        Assert.True(viewModel.HasPurpose);
    }

    [Fact]
    public void Decline_FinishesWithoutPairing()
    {
        var accepted = 0;
        var viewModel = NewViewModel(
            accept: (_, _) =>
            {
                accepted++;
                return Task.CompletedTask;
            });
        var finished = new List<bool>();
        viewModel.Finished += (_, result) => finished.Add(result);

        viewModel.DeclineCommand.Execute(null);

        Assert.Equal([false], finished);
        Assert.Equal(0, accepted);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (Exception) { }
    }

    private static ConsentViewModel NewViewModel(
        string inviter = "Ann",
        string purpose = "Fix the build",
        DateTimeOffset? expiresAt = null,
        IFolderPicker? picker = null,
        Func<RemoteToolJoinDecision, CancellationToken, Task>? accept = null) => new(
        new RemoteToolInvite(
            "inv_abcdefgh",
            inviter,
            purpose,
            new Uri("http://ann-pc:47600"),
            expiresAt),
        picker ?? new StubFolderPicker(null),
        accept ?? ((_, _) => Task.CompletedTask),
        Strings);

    private sealed class StubFolderPicker(string? result) : IFolderPicker
    {
        public Task<string?> PickAsync() => Task.FromResult(result);
    }
}
