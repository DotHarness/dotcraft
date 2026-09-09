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
    public void NewInvitation_DefaultsToFullAccessAndSuggestsAnUncreatedFolder()
    {
        var viewModel = NewViewModel();

        Assert.True(viewModel.FullAccess);
        Assert.False(viewModel.WorkspacePreferred);
        Assert.False(viewModel.CanPickFolder);
        Assert.True(Path.IsPathFullyQualified(viewModel.FolderPath));
        Assert.False(Directory.Exists(viewModel.FolderPath));
        Assert.False(viewModel.HasWarning);
        Assert.True(viewModel.CanAllow);
    }

    [Fact]
    public async Task WorkspaceCard_OpensThePickerOnlyOnTheFirstSelection()
    {
        var picked = Path.Combine(_folder, "chosen");
        Directory.CreateDirectory(picked);
        var picker = new StubFolderPicker(picked);
        var viewModel = NewViewModel(picker: picker);

        await viewModel.SelectWorkspaceModeCommand.ExecuteAsync(null);

        Assert.Equal(1, picker.Calls);
        Assert.True(viewModel.WorkspacePreferred);
        Assert.True(viewModel.CanPickFolder);
        Assert.Equal(picked, viewModel.FolderPath);

        await viewModel.SelectWorkspaceModeCommand.ExecuteAsync(null);

        Assert.Equal(1, picker.Calls);
        Assert.Equal(picked, viewModel.FolderPath);
    }

    [Fact]
    public async Task WorkspaceCard_KeepsTheSuggestedFolderWhenThePickIsCancelled()
    {
        RemoteToolJoinDecision? decision = null;
        var viewModel = NewViewModel(
            picker: new StubFolderPicker(null),
            accept: (value, _) =>
            {
                decision = value;
                return Task.CompletedTask;
            });
        var suggested = viewModel.FolderPath;

        await viewModel.SelectWorkspaceModeCommand.ExecuteAsync(null);

        Assert.True(viewModel.WorkspacePreferred);
        Assert.Equal(suggested, viewModel.FolderPath);
        Assert.False(viewModel.HasWarning);
        Assert.True(viewModel.CanAllow);

        await viewModel.AllowCommand.ExecuteAsync(null);

        Assert.Equal(suggested, decision?.WorkspacePath);
        Assert.True(decision?.CreateWorkspace);
        Assert.Equal(RemoteToolAuthorization.WorkspacePreferred, decision?.AuthorizationMode);
    }

    [Fact]
    public async Task Reauthorization_NeverOpensThePicker()
    {
        var picker = new StubFolderPicker(Path.Combine(_folder, "unused"));
        var viewModel = NewViewModel(picker: picker);
        viewModel.CanChangeFolder = false;
        viewModel.FolderPath = _folder;
        viewModel.FullAccess = false;

        await viewModel.SelectWorkspaceModeCommand.ExecuteAsync(null);

        Assert.Equal(0, picker.Calls);
        Assert.False(viewModel.CanPickFolder);
        Assert.Equal(_folder, viewModel.FolderPath);
    }

    [Fact]
    public async Task SwitchingModes_PreservesTheChosenFolder()
    {
        RemoteToolJoinDecision? decision = null;
        var viewModel = NewViewModel(accept: (value, _) =>
        {
            decision = value;
            return Task.CompletedTask;
        });
        viewModel.FullAccess = false;
        viewModel.FolderPath = _folder;

        viewModel.SelectFullAccessCommand.Execute(null);
        Assert.False(viewModel.WorkspacePreferred);
        await viewModel.AllowCommand.ExecuteAsync(null);
        Assert.Equal(RemoteToolAuthorization.FullAccess, decision?.AuthorizationMode);

        viewModel.FullAccess = false;
        Assert.True(viewModel.WorkspacePreferred);
        Assert.Equal(_folder, viewModel.FolderPath);
        Assert.True(viewModel.CanAllow);
    }

    [Fact]
    public void CanAllow_RequiresAnExistingFolderThatIsNotAWholeDriveOrProfile()
    {
        var viewModel = NewViewModel();
        viewModel.FullAccess = false;

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
    public async Task FullAccess_IsNotBlockedByAnUnusableFolder()
    {
        RemoteToolJoinDecision? decision = null;
        var viewModel = NewViewModel(accept: (value, _) =>
        {
            decision = value;
            return Task.CompletedTask;
        });
        var suggested = viewModel.FolderPath;
        viewModel.FullAccess = false;
        viewModel.FolderPath = Path.Combine(_folder, "missing");

        Assert.False(viewModel.CanAllow);
        Assert.True(viewModel.HasWarning);

        viewModel.SelectFullAccessCommand.Execute(null);

        Assert.True(viewModel.CanAllow);
        Assert.False(viewModel.HasWarning);

        await viewModel.AllowCommand.ExecuteAsync(null);

        Assert.Equal(suggested, decision?.WorkspacePath);
        Assert.True(decision?.CreateWorkspace);
    }

    [Fact]
    public async Task Allow_PairsWithTheChosenFolder()
    {
        var accepted = new List<RemoteToolJoinDecision>();
        var picked = Path.Combine(_folder, "chosen");
        Directory.CreateDirectory(picked);
        var viewModel = NewViewModel(
            picker: new StubFolderPicker(picked),
            accept: (decision, _) =>
            {
                accepted.Add(decision);
                return Task.CompletedTask;
            });
        var finished = new List<bool>();
        viewModel.Finished += (_, result) => finished.Add(result);

        await viewModel.SelectWorkspaceModeCommand.ExecuteAsync(null);
        await viewModel.AllowCommand.ExecuteAsync(null);

        Assert.Equal(picked, Assert.Single(accepted).WorkspacePath);
        Assert.False(accepted[0].CreateWorkspace);
        Assert.Equal([true], finished);
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
        Assert.Equal("Ann", NewViewModel(inviter: "Ann\r\n").InviterName);

        var capped = NewViewModel(inviter: new string('x', 400) + "\0").InviterName;

        Assert.Equal(120, capped.Length);
        Assert.DoesNotContain(capped, character => char.IsControl(character));
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
        DateTimeOffset? expiresAt = null,
        IFolderPicker? picker = null,
        Func<RemoteToolJoinDecision, CancellationToken, Task>? accept = null) => new(
        new RemoteToolInvite(
            "inv_abcdefgh",
            inviter,
            new Uri("http://ann-pc:47600"),
            expiresAt),
        picker ?? new StubFolderPicker(null),
        accept ?? ((_, _) => Task.CompletedTask),
        Strings);

    private sealed class StubFolderPicker(string? result) : IFolderPicker
    {
        public int Calls { get; private set; }

        public Task<string?> PickAsync()
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
