using System.Globalization;
using DotCraft.RemoteTools;
using DotCraft.Satellite.Localization;

namespace DotCraft.Satellite.ViewModels;

internal enum IslandMode
{
    Hidden,
    Compact,
    Running,
    Expanded,
    Approval
}

internal sealed record IslandPeerRow(
    string PeerId,
    string Name,
    string Meta,
    bool CanOpenFolder,
    string DisconnectLabel,
    string OpenFolderLabel);

/// <summary>Everything the island derives its state from, with no user interface types.</summary>
internal sealed record IslandInputs(
    SatelliteTrayState State,
    IReadOnlyList<RemoteToolPeer> Peers,
    IReadOnlyList<RemoteToolActivity> Activities,
    IReadOnlyList<IslandApprovalEntry> Approvals,
    bool Pinned,
    bool PointerOver,
    DateTimeOffset Now);

/// <summary>Every value the view renders; the view binds to this and holds no logic of its own.</summary>
internal sealed record IslandStateModel
{
    public const int CommandCap = 48;

    /// <summary>A peer or inviter name is attacker-influenced text, so it is capped before layout.</summary>
    public const int NameCap = 120;

    public IslandMode Mode { get; init; } = IslandMode.Hidden;

    public bool Visible => Mode != IslandMode.Hidden;

    public double Width => Mode switch
    {
        IslandMode.Approval => 380,
        IslandMode.Compact => 240,
        _ => 340
    };

    public bool IsPanelShape => Mode is IslandMode.Expanded or IslandMode.Approval;

    public bool IsApproval => Mode == IslandMode.Approval;

    public bool ShowRunning { get; init; }

    public string CompactLabel { get; init; } = string.Empty;

    public string SinceLabel { get; init; } = string.Empty;

    public string OperationLabel { get; init; } = string.Empty;

    public string CommandPreview { get; init; } = string.Empty;

    public string CommandFull { get; init; } = string.Empty;

    public string ElapsedLabel { get; init; } = string.Empty;

    public int Concurrency { get; init; }

    public string PanelTitle { get; init; } = string.Empty;

    public string PanelCount { get; init; } = string.Empty;

    public string PauseLabel { get; init; } = string.Empty;

    public IReadOnlyList<IslandPeerRow> Peers { get; init; } = [];

    public string ApprovalTitle { get; init; } = string.Empty;

    public string ApprovalOperation { get; init; } = string.Empty;

    public string ApprovalTarget { get; init; } = string.Empty;

    public string ApprovalTargetFull { get; init; } = string.Empty;

    public string CountdownLabel { get; init; } = string.Empty;

    /// <summary>How much of the two-minute window is left, as a fraction the ring draws.</summary>
    public double CountdownFraction { get; init; }

    public int Queued { get; init; }

    public string DenyLabel { get; init; } = string.Empty;

    public string AllowLabel { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    /// <summary>The expanded panel is an addition to the header, not a replacement for it.</summary>
    public static IslandStateModel Derive(IslandInputs inputs, SatelliteStrings strings)
    {
        var peers = inputs.Peers.Where(peer => peer.ConnectedSince is not null).ToArray();
        if (inputs.State != SatelliteTrayState.Connected || peers.Length == 0)
            return new IslandStateModel();

        var activity = inputs.Activities.Count > 0 ? inputs.Activities[^1] : null;
        var approval = inputs.Approvals.Count > 0 ? inputs.Approvals[0] : null;
        var mode = approval is not null ? IslandMode.Approval
            : inputs.Pinned || inputs.PointerOver ? IslandMode.Expanded
            : activity is not null ? IslandMode.Running
            : IslandMode.Compact;

        var machines = strings.Format("island.machines", peers.Length);
        var since = peers.Length == 1 ? Since(peers[0], strings) : string.Empty;
        var model = new IslandStateModel
        {
            Mode = mode,
            ShowRunning = activity is not null,
            CompactLabel = peers.Length > 1 ? machines : Cap(peers[0].DisplayName),
            SinceLabel = since,
            PanelTitle = strings["tray.status.connected"],
            PanelCount = peers.Length > 1 ? machines : since,
            PauseLabel = strings["tray.pause"],
            Peers = [.. peers.Select(peer => Row(peer, strings))],
            DenyLabel = strings["consent.decline"],
            AllowLabel = strings["approval.once"]
        };

        if (activity is not null)
        {
            var command = activity.CommandPreview ?? activity.ToolName;
            model = model with
            {
                OperationLabel = Label(Operation(activity.ToolName), activity.ToolName, strings),
                CommandFull = command,
                CommandPreview = Clip(command, CommandCap),
                ElapsedLabel = Clock(inputs.Now - activity.StartedAt),
                Concurrency = inputs.Activities.Count - 1
            };
        }

        if (approval is not null)
        {
            var left = IslandApprovalQueue.Window - (inputs.Now - approval.ReceivedAt);
            model = model with
            {
                ApprovalTitle = strings.Format("approval.inviter", Cap(approval.Request.InviterName)),
                ApprovalOperation = Label(
                    approval.Request.Operation, approval.Request.Operation, strings),
                ApprovalTargetFull = approval.Request.Target,
                ApprovalTarget = Clip(approval.Request.Target, CommandCap),
                CountdownLabel = Clock(left),
                CountdownFraction = Math.Clamp(
                    left.TotalSeconds / IslandApprovalQueue.Window.TotalSeconds, 0, 1),
                Queued = inputs.Approvals.Count - 1
            };
        }

        return model with { Summary = strings["tray.status.connected"] + " · " + Headline(model) };
    }

    private static string Headline(IslandStateModel model) => model.Mode switch
    {
        IslandMode.Approval => model.ApprovalTitle,
        _ when model.ShowRunning => model.OperationLabel + " · " + model.CommandPreview,
        _ => model.CompactLabel
    };

    private static IslandPeerRow Row(RemoteToolPeer peer, SatelliteStrings strings)
    {
        var name = Cap(peer.DisplayName);
        return new IslandPeerRow(
            peer.PeerId,
            name,
            strings[peer.AuthorizationMode switch
            {
                RemoteToolAuthorization.FullAccess => "consent.full",
                RemoteToolAuthorization.WorkspacePreferred => "consent.folderHeading",
                _ => "consent.review"
            }] + " · " + Since(peer, strings),
            !string.IsNullOrEmpty(peer.WorkspacePath),
            strings["island.disconnect"] + " · " + name,
            strings["island.openFolder"] + " · " + name);
    }

    private static string Since(RemoteToolPeer peer, SatelliteStrings strings) =>
        peer.ConnectedSince is { } connected
            ? strings.Format(
                "island.since",
                connected.ToLocalTime().ToString("t", CultureInfo.CurrentCulture))
            : string.Empty;

    /// <summary>Names a tool in the vocabulary the approval request already uses.</summary>
    private static string Operation(string toolName) => toolName switch
    {
        "Exec" => "execute",
        "WriteStdin" => "input",
        "LSP" => "languageServer",
        "WriteFile" => "write",
        "EditFile" => "edit",
        "ReadFile" or "GrepFiles" or "FindFiles" => "read",
        _ => toolName
    };

    private static string Label(string operation, string fallback, SatelliteStrings strings) =>
        strings["approval.operation." + operation] is { Length: > 0 } label ? label : fallback;

    private static string Cap(string value) =>
        value.Length <= NameCap ? value : value[..NameCap];

    private static string Clip(string value, int cap) =>
        value.Length <= cap ? value : value[..(cap - 1)] + "…";

    private static string Clock(TimeSpan span)
    {
        var seconds = Math.Max(0, (int)span.TotalSeconds);
        return string.Create(
            CultureInfo.CurrentCulture, $"{seconds / 60}:{seconds % 60:D2}");
    }
}
