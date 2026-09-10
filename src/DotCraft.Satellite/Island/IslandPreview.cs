using DotCraft.RemoteTools;
using DotCraft.Satellite.ViewModels;

namespace DotCraft.Satellite.Island;

/// <summary>The fixture data behind <c>--preview-island</c>, so one island state can be reviewed without a pairing.</summary>
internal static class IslandPreview
{
    public static void Show(string scenario, IslandViewModel island)
    {
        var now = DateTimeOffset.Now;
        var ann = Peer("pair_ann", "Ann's workstation", RemoteToolAuthorization.WorkspacePreferred, 11, 23, now);
        var priya = Peer("pair_priya", "Priya's desktop", RemoteToolAuthorization.FullAccess, 14, 5, now);
        var bay = Peer(
            "pair_bay4",
            "A machine whose owner gave it a name far longer than the capsule can show",
            RemoteToolAuthorization.WorkspacePreferred,
            9,
            41,
            now);

        const string longCommand =
            "pwsh -NoProfile -Command \"Get-ChildItem -Recurse -Filter *.csproj | Measure-Object -Property Length -Sum\"";
        var build = new RemoteToolActivity(
            ann.PeerId, "Exec", "npm run build -- --profile", now.AddSeconds(-12));
        var sweep = new RemoteToolActivity(bay.PeerId, "Exec", longCommand, now.AddSeconds(-161));

        switch (scenario)
        {
            case "running":
                island.Freeze([ann], [build], [], pinned: false);
                break;
            case "multi":
                island.Freeze([ann, priya], [], [], pinned: false);
                break;
            case "expanded":
                island.Freeze([ann, priya], [build], [], pinned: true);
                break;
            case "approval":
                island.Freeze([ann], [build], [Request(ann, "execute", "npm run test -- --runInBand", now, 13)], pinned: false);
                break;
            case "approval-queue":
                island.Freeze(
                    [ann],
                    [build],
                    [
                        Request(ann, "execute", "npm run test -- --runInBand", now, 13),
                        Request(ann, "edit", "src/app/components/Editor.tsx", now, 2),
                        Request(ann, "input", "y", now, 0)
                    ],
                    pinned: false);
                break;
            case "long-name":
                island.Freeze(
                    [bay], [sweep], [Request(bay, "execute", longCommand, now, 7)], pinned: true);
                break;
            default:
                island.Freeze([ann], [], [], pinned: false);
                break;
        }
    }

    private static RemoteToolPeer Peer(
        string peerId, string name, string mode, int hour, int minute, DateTimeOffset now) => new(
        peerId,
        name,
        "workspace",
        @"C:\workspaces\example",
        now.AddDays(-3),
        new DateTimeOffset(now.Year, now.Month, now.Day, hour, minute, 0, now.Offset),
        mode);

    private static IslandApprovalEntry Request(
        RemoteToolPeer peer, string operation, string target, DateTimeOffset now, int ageSeconds) =>
        new(
            new RemoteToolApprovalRequest(
                peer.PeerId,
                "Ann",
                1,
                Guid.NewGuid().ToString("N"),
                operation == "execute" ? "shell" : "file",
                operation,
                target,
                peer.WorkspacePath),
            now.AddSeconds(-ageSeconds));
}
