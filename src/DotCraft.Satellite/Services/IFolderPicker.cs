using System.Runtime.Versioning;
using Windows.Storage.Pickers;

namespace DotCraft.Satellite.Services;

internal interface IFolderPicker
{
    Task<string?> PickAsync();
}

/// <summary>
/// The picker needs the window that owns it, and the view model exists before that window does, so
/// the handle is resolved at the moment the owner asks to change the folder.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class WindowFolderPicker(Func<nint> windowHandle) : IFolderPicker
{
    public async Task<string?> PickAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
            SettingsIdentifier = "DotCraftSatelliteSharedFolder"
        };
        picker.FileTypeFilter.Add("*");
        var handle = windowHandle();
        if (handle == 0)
            return null;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
