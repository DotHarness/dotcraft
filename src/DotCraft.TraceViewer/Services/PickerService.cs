using Windows.Storage.Pickers;

namespace DotCraft.TraceViewer.Services;

internal static class PickerService
{
    public static async Task<string?> PickWorkspaceAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
            SettingsIdentifier = "DotCraftTraceViewerWorkspace",
        };
        picker.FileTypeFilter.Add("*");

        var handle = ((App)Microsoft.UI.Xaml.Application.Current).WindowHandle;
        if (handle == 0)
            throw new InvalidOperationException("The DotCraft Trace Viewer window is not ready for a folder picker.");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
