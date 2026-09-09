using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotCraft.RemoteTools;
using DotCraft.Satellite.Localization;
using DotCraft.Satellite.Services;

namespace DotCraft.Satellite.ViewModels;

internal sealed partial class ConsentViewModel : ObservableObject
{
    private const int MaxNameLength = 120;

    private readonly RemoteToolInvite _invite;
    private readonly IFolderPicker _picker;
    private readonly Func<RemoteToolJoinDecision, CancellationToken, Task> _accept;
    private readonly SatelliteStrings _strings;
    private readonly string _defaultFolder;

    [ObservableProperty]
    public partial bool FullAccess { get; set; }

    [ObservableProperty]
    public partial string FolderPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Warning { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public ConsentViewModel(
        RemoteToolInvite invite,
        IFolderPicker picker,
        Func<RemoteToolJoinDecision, CancellationToken, Task> accept,
        SatelliteStrings strings)
    {
        _invite = invite;
        _defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "DotCraft", "Satellite", "task-" + Guid.NewGuid().ToString("N")[..8]);
        _picker = picker;
        _accept = accept;
        _strings = strings;
        FolderPath = _defaultFolder;
        FullAccess = true;
        InviterName = Sanitize(invite.InviterDisplayName, MaxNameLength);
        if (IsExpired)
            Warning = strings["consent.warningExpired"];
    }

    public event EventHandler<bool>? Finished;

    public string InviterName { get; }

    public string WindowTitle => _strings["consent.windowTitle"];

    public string Title => _strings.Format("consent.title", InviterName);

    public string HubLine => _strings.Format("consent.hub", _invite.HubEndpoint.Authority);

    public string FolderHeading => _strings["consent.folderHeading"];

    public string ChangeFolderText => _strings["consent.folderChange"];

    public string AllowText => _strings["consent.allow"];

    public string DeclineText => _strings["consent.decline"];

    public bool HasWarning => Warning.Length > 0;

    public bool IsExpired => _invite.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow;

    public string ModeHeading => _strings["consent.modeHeading"];
    public string PreferredText => _strings["consent.preferred"];
    public string PreferredDescription => _strings["consent.preferredDescription"];
    public string FullText => _strings["consent.full"];
    public string FullDescription => _strings["consent.fullDescription"];
    public string FullNarration => _strings.Format("consent.fullNarration", FullText, FullDescription);
    public string PreferredNarration => _strings.Format("consent.preferredNarration", PreferredText, PreferredDescription);
    public bool WorkspacePreferred => !FullAccess;
    public bool CanChangeFolder { get; set; } = true;
    public bool CanPickFolder => CanChangeFolder && WorkspacePreferred;

    private bool FolderIsUsable => FolderPath == _defaultFolder || IsShareableFolder(FolderPath);

    /// <summary>Full access is not scoped to a folder, so an unusable one must not block it.</summary>
    private string EffectiveFolder => FolderIsUsable ? FolderPath : _defaultFolder;

    public bool CanAllow => !IsBusy && !IsExpired && (FullAccess || FolderIsUsable);

    partial void OnFullAccessChanged(bool value)
    {
        OnPropertyChanged(nameof(WorkspacePreferred));
        OnPropertyChanged(nameof(CanPickFolder));
        RefreshWarning();
        OnPropertyChanged(nameof(CanAllow));
    }

    [RelayCommand]
    private void SelectFullAccess() => FullAccess = true;

    /// <summary>
    /// The picker opens on the unselected to selected transition alone, so clicking the card again,
    /// or restoring the mode of an existing pairing, never reopens it.
    /// </summary>
    [RelayCommand]
    private async Task SelectWorkspaceModeAsync()
    {
        if (!FullAccess)
            return;
        FullAccess = false;
        if (CanChangeFolder)
            await ChangeFolderAsync();
    }

    [RelayCommand]
    private async Task ChangeFolderAsync()
    {
        var picked = await _picker.PickAsync();
        if (!string.IsNullOrWhiteSpace(picked))
            FolderPath = picked;
    }

    [RelayCommand]
    private async Task AllowAsync()
    {
        if (!CanAllow)
            return;
        IsBusy = true;
        try
        {
            var folder = EffectiveFolder;
            await _accept(new RemoteToolJoinDecision(_invite, folder,
                FullAccess ? RemoteToolAuthorization.FullAccess : RemoteToolAuthorization.WorkspacePreferred,
                folder == _defaultFolder), CancellationToken.None);
            Finished?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            Warning = _strings.Format("consent.failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Decline() => Finished?.Invoke(this, false);

    partial void OnFolderPathChanged(string value)
    {
        RefreshWarning();
        OnPropertyChanged(nameof(CanAllow));
        AllowCommand.NotifyCanExecuteChanged();
    }

    private void RefreshWarning() =>
        Warning = IsExpired ? _strings["consent.warningExpired"]
            : FullAccess ? string.Empty
            : FolderWarning(FolderPath);

    private string FolderWarning(string value)
    {
        if (value.Length == 0 || value == _defaultFolder)
            return string.Empty;
        if (!IsExistingDirectory(value))
            return _strings["consent.warningFolder"];
        return IsTooBroad(value) ? _strings["consent.warningRoot"] : string.Empty;
    }

    partial void OnWarningChanged(string value) => OnPropertyChanged(nameof(HasWarning));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanAllow));

    private static bool IsShareableFolder(string path) =>
        IsExistingDirectory(path) && !IsTooBroad(path);

    private static bool IsExistingDirectory(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path)
        && Directory.Exists(path);

    /// <summary>A whole drive or the whole user profile is never an intended share.</summary>
    private static bool IsTooBroad(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? string.Empty);
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            return true;
        var profile = Path.TrimEndingDirectorySeparator(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        return profile.Length > 0 && string.Equals(full, profile, StringComparison.OrdinalIgnoreCase);
    }

    private static string Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var cleaned = new string([.. value.Where(character => !char.IsControl(character))]).Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }
}
