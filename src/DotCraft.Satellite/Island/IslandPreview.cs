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
                island.Freeze(new[] { ann }, new[] { build }, []);
                break;
            case "multi":
                island.Freeze(new[] { ann, priya }, [], []);
                break;
            case "multi-running":
                island.Freeze(new[] { ann, priya }, new[] { build }, []);
                break;
            case "approval":
                island.Freeze(
                    new[] { ann },
                    new[] { build },
                    new[] { Request(ann, "execute", "npm run test -- --runInBand", now, 13) });
                break;
            case "approval-queue":
                island.Freeze(
                    new[] { ann },
                    new[] { build },
                    new[]
                    {
                        Request(ann, "execute", "npm run test -- --runInBand", now, 13),
                        Request(ann, "edit", "src/app/components/Editor.tsx", now, 2),
                        Request(ann, "input", "y", now, 0)
                    });
                break;
            case "standby":
                island.Freeze(new[] { ann }, [], [], state: SatelliteTrayState.Standby);
                break;
            case "paused":
                island.Freeze(new[] { ann }, [], [], state: SatelliteTrayState.Paused);
                break;
            case "offline":
                island.Freeze(new[] { ann }, [], [], state: SatelliteTrayState.Offline);
                break;
            case "long-name":
                island.Freeze(
                    new[] { bay },
                    new[] { sweep },
                    new[] { Request(bay, "execute", longCommand, now, 7) });
                break;
            default:
                island.Freeze(new[] { ann }, [], []);
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