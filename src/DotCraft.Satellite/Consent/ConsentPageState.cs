using DotCraft.Satellite.ViewModels;

namespace DotCraft.Satellite.Consent;

internal sealed record ConsentPageState(
    string Theme,
    string Locale,
    bool ReduceMotion,
    string Title,
    string Hub,
    string ModeHeading,
    ConsentModeState Full,
    ConsentFolderState Folder,
    string Warning,
    string Decline,
    string Allow,
    bool CanAllow,
    bool Busy)
{
    public static ConsentPageState From(
        ConsentViewModel viewModel,
        string theme,
        string locale,
        bool reduceMotion) => new(
        theme,
        locale,
        reduceMotion,
        viewModel.Title,
        viewModel.HubLine,
        viewModel.ModeHeading,
        new ConsentModeState(viewModel.FullText, viewModel.FullDescription, viewModel.FullAccess),
        new ConsentFolderState(
            viewModel.PreferredText,
            viewModel.PreferredDescription,
            viewModel.WorkspacePreferred,
            viewModel.FolderHeading,
            viewModel.FolderPath,
            viewModel.ChangeFolderText,
            viewModel.CanPickFolder),
        viewModel.Warning,
        viewModel.DeclineText,
        viewModel.AllowText,
        viewModel.CanAllow,
        viewModel.IsBusy);
}

internal sealed record ConsentModeState(string Title, string Description, bool Selected);

internal sealed record ConsentFolderState(
    string Title,
    string Description,
    bool Selected,
    string FolderHeading,
    string Path,
    string Change,
    bool CanPickFolder);
