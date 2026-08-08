# Configure Oratorio

Open the Board and select **Oratorio settings** to manage source connections, project routes, Agent execution, and automation.

![Oratorio settings in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/settings-light.png)

## Providers and projects

Configure GitHub and GitLab credentials on their provider pages. Add each repository or project separately, then map it to the DotCraft workspace that contains the matching checkout. Oratorio does not guess a fallback workspace for source-backed work.

Provider pages show read, write, and webhook health. Use **Sync now** for an immediate update or set a schedule for periodic synchronization. Use a full repair only when the source needs a complete reconciliation.

## Agent execution and worktrees

The root page controls approval policy, run timeout, managed Worktree location and branch naming, automatic dispatch, review automation, and delivery behavior. Runtime concurrency, retry, stall, and cleanup policies remain Server-managed and are not exposed in Desktop.

Managed worktrees use the repository-local default root:

```text
<repositoryWorkspace>/.craft/oratorio/worktrees
```

Managed branches use `oratorio/run/<work-item-key>`. Let Oratorio clean up its own worktrees; cleanup observes persisted run ownership rather than deleting directories by age alone.

## Saving and secrets

Settings save after a short delay. A field shows its pending or failed state, and a failed save can be retried. If another editor changes the same revision, Desktop reloads the confirmed server configuration instead of presenting an unconfirmed local success.

Saved secrets are write-only. The secret editor provides three explicit choices:

- **Keep** the stored value.
- **Replace** it with a new value.
- **Clear** the stored value.

Some runtime settings require an Oratorio Server restart. Settings reports that state after the configuration is saved.

When Desktop is connected to a remote DotCraft Stack, administrative settings are read-only. Board operations, source sync, and task actions remain available.

## Related docs

- [Oratorio](../oratorio)
- [Connect GitHub](./github)
- [Connect GitLab](./gitlab)
- [Deploy the DotCraft Stack](../self-hosted/server-deployment)
