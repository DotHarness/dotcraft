using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotCraft.RemoteTools;
using DotCraft.Satellite.Localization;
using DotCraft.Satellite.Services;

namespace DotCraft.Satellite.ViewModels;

internal sealed partial class ConsentViewModel : ObservableObject
{
    private const int MaxPurposeLength = 280;
    private const int MaxNameLength = 120;

    private readonly RemoteToolInvite _invite;
    private readonly IFolderPicker _picker;
    private readonly Func<string, CancellationToken, Task> _accept;
    private readonly SatelliteStrings _strings;

    [ObservableProperty]
    public partial string FolderPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Warning { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public ConsentViewModel(
        RemoteToolInvite invite,
        IFolderPicker picker,
        Func<string, CancellationToken, Task> accept,
        SatelliteStrings strings)
    {
        _invite = invite;
        _picker = picker;
        _accept = accept;
        _strings = strings;
        InviterName = Sanitize(invite.InviterDisplayName, MaxNameLength);
        Purpose = Sanitize(invite.Purpose, MaxPurposeLength);
        if (IsExpired)
            Warning = strings["consent.warningExpired"];
    }

    public event EventHandler<bool>? Finished;

    public string InviterName { get; }

    public string Purpose { get; }

    public bool HasPurpose => Purpose.Length > 0;

    public string WindowTitle => _strings["consent.windowTitle"];

    public string Title => _strings.Format("consent.title", InviterName);

    public string HubLine => _strings.Format("consent.hub", _invite.HubEndpoint.Authority);

    public string PurposeHeading => _strings["consent.purposeHeading"];

    public string FolderHeading => _strings["consent.folderHeading"];

    public string ChangeFolderText => _strings["consent.folderChange"];

    public string GrantsHeading => _strings.Format("consent.grantsHeading", InviterName);

    public string GrantFiles => _strings["consent.grantFiles"];

    public string GrantCommands => _strings["consent.grantCommands"];

    public string GrantSignedIn => _strings["consent.grantSignedIn"];

    public string AllowText => _strings["consent.allow"];

    public string DeclineText => _strings["consent.decline"];

    public bool HasWarning => Warning.Length > 0;

    public bool IsExpired => _invite.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow;

    public bool CanAllow => !IsBusy && !IsExpired && IsShareableFolder(FolderPath);

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
            await _accept(FolderPath, CancellationToken.None);
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
        if (!IsExpired)
            Warning = FolderWarning(value);
        OnPropertyChanged(nameof(HasWarning));
        OnPropertyChanged(nameof(CanAllow));
        AllowCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Nothing is pre-filled, so an empty folder is where the owner starts, not a mistake.</summary>
    private string FolderWarning(string value)
    {
        if (value.Length == 0)
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
