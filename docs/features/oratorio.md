# Oratorio

Oratorio is DotCraft's built-in project board. Local tasks, GitHub issues and pull requests, and GitLab issues and merge requests all land on the same Board. Hand work to an Agent, follow the run, review the result, and deliver approved changes back to the provider, all without leaving [DotCraft Desktop](./entry-points/desktop).

![Oratorio board in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/board-light.png)

## Start with a local task

You can run Oratorio before connecting any provider:

1. Open a Git repository as a project in DotCraft Desktop.
2. Select **Oratorio** in the sidebar.
3. Select **New local task**, describe the problem, and choose the repository and base branch.
4. Open the card and pick a run mode under **Start Agent work**.
5. Follow the run in Quick View, then open the full task once it is ready for review.

Agent work happens in a worktree Oratorio creates for the run, so the checkout you are using stays untouched. For the full path from filtering the Board to reviewing and delivering, see the [Oratorio workflow](./oratorio/workflow).

## Connect GitHub and GitLab

Add a project in Oratorio settings and work from your provider syncs onto the same Board. [GitHub](./oratorio/github) connects through a GitHub App, and [GitLab](./oratorio/gitlab) through a project-scoped token.

Oratorio writes nothing back to a provider until you enable it. A public webhook endpoint is optional, since manual and scheduled sync work without one. Project-to-workspace mapping, Agent execution, and automation are set in [Configure Oratorio](./oratorio/settings).

## Use Oratorio in a conversation

Connect Oratorio to a conversation to read and move tasks from the chat itself.

1. Install Oratorio, open its plugin details, and select **Connect** to connect the current workspace.

   ![Connect Oratorio from its plugin details](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/app-connection-light.png)

2. Open the target conversation, select **Apps**, and enable Oratorio.

   ![Enable Oratorio in a conversation's Apps picker](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/thread-app-light.png)

Turning Oratorio off in **Apps** affects only that conversation. **Disconnect** in the plugin details revokes the whole workspace connection along with its conversation bindings. See [Connected Apps](./agent-system/connected-apps) for the full connection and authorization flow.

## Local and remote deployments

In local mode, Desktop starts the bundled Oratorio Server the first time you open the feature. There is nothing extra to install.

For a remote deployment, connect Desktop to a [DotCraft Stack](./self-hosted/server-deployment). The Board and task operations are identical. Server administration settings are read-only over a remote connection.

## Related docs

- [Oratorio workflow](./oratorio/workflow) — the full path from finding a task on the Board to reviewing and delivering it
- [Configure Oratorio](./oratorio/settings) — provider connections, project mapping, and Agent execution policy
