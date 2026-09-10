using System.Globalization;
using System.Text.Json;
using DotCraft.RemoteTools;
using DotCraft.Satellite.Consent;
using DotCraft.Satellite.Localization;
using DotCraft.Satellite.Services;
using DotCraft.Satellite.ViewModels;
using DotCraft.Satellite.Web;
using Xunit;

namespace DotCraft.Satellite.Tests;

public sealed class ConsentPageStateTests
{
    private static readonly SatelliteStrings Strings =
        SatelliteStrings.For("en", CultureInfo.InvariantCulture);

    [Fact]
    public void Projection_SelectsTheCardTheViewModelIsOn()
    {
        var viewModel = NewViewModel();

        var full = ConsentPageState.From(viewModel, "light", "zh-Hans", reduceMotion: true);

        Assert.True(full.Full.Selected);
        Assert.False(full.Folder.Selected);

        viewModel.FullAccess = false;

        var folder = ConsentPageState.From(viewModel, "dark", "en", reduceMotion: false);

        Assert.False(folder.Full.Selected);
        Assert.True(folder.Folder.Selected);
    }

    [Fact]
    public void Serialization_NamesEveryFieldTheWayThePageReadsIt()
    {
        var json = JsonSerializer.Serialize(
            ConsentPageState.From(NewViewModel(), "dark", "en", reduceMotion: false),
            SatellitePageHost.JsonOptions);

        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("canAllow").GetBoolean());
        Assert.False(document.RootElement.GetProperty("folder").GetProperty("canPickFolder").GetBoolean());
    }

    private static ConsentViewModel NewViewModel() => new(
        new RemoteToolInvite("inv_abcdefgh", "Ann", new Uri("http://ann-pc:47600"), null),
        new NoFolderPicker(),
        (_, _) => Task.CompletedTask,
        Strings);

    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickAsync() => Task.FromResult<string?>(null);
    }
}
