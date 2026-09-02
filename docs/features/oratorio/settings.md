# Configure Oratorio

Open the Board and select **Oratorio settings** to manage source connections, project routes, Agent execution, and automation.

![Oratorio settings in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/settings-light.png)

## Sources and projects

**Connect a source** walks through credentials, the repository or project, its DotCraft workspace, and automation in one pass, then confirms read access with a first sync. The GitHub and GitLab provider pages hold the same settings for later changes. Each repository and project maps to the DotCraft workspace that holds the matching checkout, and source-backed work never falls through to another workspace.

When a mapped Workspace goes offline or is no longer registered in DotCraft, the binding stays visible, marked unavailable. Rebind it to an open local Workspace, or remove the project. Removing a project stops future sync, automation, and dispatch, and keeps the existing task history. An unavailable binding doesn't block other settings changes — Oratorio checks again when it reports status or starts a run.

Provider pages show read, write, and webhook status. Select **Sync now** for one immediate sync, or set a sync schedule.

## Agent execution and worktrees

How the Agent runs, from when it pauses for approval to how finished work is delivered, is set on the main Oratorio settings page. These values are read when a new run starts, so a change only affects later runs.

Managed worktrees are created inside the repository by default:

```text
<repositoryWorkspace>/.craft/oratorio/worktrees
```

Their branches start with `oratorio/run/`. Let Oratorio clean these worktrees up itself — it reclaims them by what a run actually holds, not by how long a directory has been there.

## Saving and secrets

Settings save on their own once you stop editing. A field shows its saving or failed state, and a failed save can be retried. If the same configuration changed elsewhere, Desktop reloads the values the server confirmed instead of showing an unconfirmed success.

Saved secrets are write-only and never shown again in plain text. Choose **Replace secret** to store a new value, or **Clear secret** to empty it. Doing nothing keeps what's already there.

Some runtime settings need an Oratorio Server restart. Settings tells you once the configuration is saved.

When Desktop is connected to a remote [DotCraft Stack](../self-hosted/server-deployment), administrative settings are read-only. Board operations, source sync, and task actions still work.

## Related docs

- [Connect GitHub](./github) — sync issues and pull requests with a GitHub App
- [Connect GitLab](./gitlab) — sync issues and merge requests with a project token
