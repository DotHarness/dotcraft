using System.Runtime.Versioning;
using DotCraft.RemoteTools;
using DotCraft.Satellite.Localization;
using DotCraft.Satellite.Services;
using DotCraft.Satellite.ViewModels;

namespace DotCraft.Satellite.Consent;

[SupportedOSPlatform("windows10.0.19041.0")]
internal static class ConsentPreview
{
    private const string ExampleFolder = @"D:\example\work";

    /// <summary>An existing folder that names no user, so a chosen folder can be shown without a warning.</summary>
    private static readonly string PublicFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);

    public static ConsentWindow Show(string scenario, SatelliteStrings strings)
    {
        ConsentWindow? window = null;
        var picker = new WindowFolderPicker(() => WinRT.Interop.WindowNative.GetWindowHandle(window!));
        window = new ConsentWindow(Create(scenario, strings, picker));
        window.Activate();
        return window;
    }

    private static ConsentViewModel Create(string scenario, SatelliteStrings strings, IFolderPicker picker)
    {
        var model = new ConsentViewModel(Invite(scenario), picker, (_, _) => Task.CompletedTask, strings);
        switch (scenario)
        {
            case "folder-selected":
                model.FullAccess = false;
                model.FolderPath = PublicFolder;
                break;
            case "folder-warning":
                model.FullAccess = false;
                model.FolderPath = Path.GetPathRoot(Path.GetTempPath()) ?? ExampleFolder;
                break;
            case "folder-missing":
                model.FullAccess = false;
                model.FolderPath = ExampleFolder;
                break;
            case "busy":
                model.IsBusy = true;
                break;
            case "failed":
                model.Warning = strings.Format("consent.failed", "the Hub refused the invitation");
                break;
            case "manage-access":
                model.CanChangeFolder = false;
                model.FolderPath = PublicFolder;
                break;
        }
        return model;
    }

    private static RemoteToolInvite Invite(string scenario) => scenario switch
    {
        "expired" => new RemoteToolInvite(
            "inv_abcdefgh", "Ann Fischer", new Uri("http://ann-pc:47600"), DateTimeOffset.UtcNow.AddMinutes(-3)),
        "manage-access" => new RemoteToolInvite("", "Ann Fischer", new Uri("http://localhost"), null),
        "long-inviter-name" => new RemoteToolInvite(
            "inv_abcdefgh",
            "Ann Fischer from the platform reliability group in the Rotterdam office, reachable through the shared inbox",
            new Uri("http://a-very-long-machine-name-from-the-rotterdam-office:47600"),
            null),
        _ => new RemoteToolInvite(
            "inv_abcdefgh", "Ann Fischer", new Uri("http://ann-pc:47600"), DateTimeOffset.UtcNow.AddHours(2))
    };
}
