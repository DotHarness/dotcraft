# Oratorio

Oratorio is DotCraft's built-in project board for local tasks, GitHub issues and pull requests, and GitLab issues and merge requests. Use it to hand work to agents, follow each run, review the result, and deliver approved changes without leaving DotCraft Desktop.

![Oratorio board in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/board-light.png)

## Start with a local task

1. Open a Git repository as a project in DotCraft Desktop.
2. Select **Oratorio** in the sidebar.
3. Select **New local task**, then enter the problem and choose the repository and base branch.
4. Open the card and choose an available action under **Start Agent work**.
5. Follow the run in Quick View or open the full task when it is ready for review.

Oratorio creates an isolated managed worktree for Agent work. Your active checkout remains unchanged while the run is in progress.

## Connect external work

Add a project in Oratorio settings to synchronize work from a source provider:

- [Connect GitHub](./oratorio/github) with a GitHub App.
- [Connect GitLab](./oratorio/gitlab) with a project-scoped token.

Source writes are disabled until you explicitly enable them. Manual and scheduled sync work without a public webhook endpoint.

## Use Oratorio in a conversation

1. Install Oratorio, open its plugin details, and select **Connect** to connect the current workspace.

   ![Connect Oratorio from its plugin details](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/app-connection-light.png)

2. Open the target conversation, select **Apps**, and enable Oratorio.

   ![Enable Oratorio in a conversation's Apps picker](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/thread-app-light.png)

Turning off Oratorio in **Apps** only removes it from that conversation. **Disconnect** in the plugin details revokes the current workspace connection and its related conversation bindings. See [Connected Apps](./agent-system/connected-apps) for the complete connection and authorization flow.

## Local and remote use

In local mode, Desktop starts the bundled Oratorio Server when you first open the feature. DotCraft Hub manages the process, and Oratorio stores its user-level state under `~/.craft/oratorio/`.

For a remote deployment, connect Desktop to a [DotCraft Stack](./self-hosted/server-deployment). The same Board and task operations remain available. Server administration settings are read-only from a remote Desktop connection.

## Related docs

- [Follow the Oratorio workflow](./oratorio/workflow)
- [Configure Oratorio](./oratorio/settings)
- [Connected Apps](./agent-system/connected-apps)
- [Deploy the DotCraft Stack](./self-hosted/server-deployment)
- [Desktop](./entry-points/desktop)
