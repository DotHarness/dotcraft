# Oratorio

Oratorio is DotCraft's built-in project board. Local tasks, GitHub issues and pull requests, and GitLab issues and merge requests all land on the same Board. Hand work to an Agent, follow the run, review the result, and deliver approved changes back to the provider, all without leaving [DotCraft Desktop](../../start/desktop).

![Oratorio board in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/board.png)

## Start with a local task

You can run Oratorio before connecting any provider:

1. Open a Git repository as a project in DotCraft Desktop.
2. Select **Oratorio** in the sidebar.
3. Select **New local task**, describe the problem, and choose the repository and base branch.
4. Open the card and start a run with the button at the top of the task.
5. Follow the run in the task's Activity, and review it there once it is ready.

Agent work happens in a worktree Oratorio creates for the run, so the checkout you are using stays untouched. For the full path from filtering the Board to reviewing and delivering, see the [Oratorio workflow](./workflow).

## Connect GitHub and GitLab

Select **Connect GitHub** or **Connect GitLab** on the Board and the guided flow takes you from credentials to a first sync. [GitHub](./github) connects through a GitHub App, and [GitLab](./gitlab) through a project-scoped token. You can also [connect a repository from a conversation](#use-oratorio-in-a-conversation).

Oratorio writes nothing back to a provider until you enable it. A public webhook endpoint is optional, since manual and scheduled sync work without one. Project-to-workspace mapping, Agent execution, and automation are set in [Configure Oratorio](./settings).

## Use Oratorio in a conversation

Connect Oratorio to a conversation to read and move tasks from the chat itself.

1. Install Oratorio, open its plugin details, and select **Connect** to connect the current workspace.

   ![Connect Oratorio from its plugin details](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/app-connection.png)

2. Start a new conversation. Oratorio is available in it from the first message.

In that conversation you can also connect a repository by asking. Send `$oratorio-setup` from the repository's project, or ask the Agent to connect the repository to Oratorio. The Agent reads the repository's remote, confirms the sync and review settings with you, and saves the connection. When a token or private key is needed, a form appears in the conversation. What you enter there goes straight to Oratorio, and the Agent never sees it.

**Disconnect** in the plugin details removes Oratorio from every conversation in the workspace. See [Connected Apps](../connected-apps) for the full connection and authorization flow.

## Local and remote deployments

In local mode, Desktop starts the bundled Oratorio Server the first time you open the feature. There is nothing extra to install.

For a remote deployment, connect Desktop to a [DotCraft Stack](../../self-hosting/server-deployment). The Board and task operations are identical. Server administration settings are read-only over a remote connection.

## Related docs

- [Oratorio workflow](./workflow) — the full path from finding a task on the Board to reviewing and delivering it
- [Configure Oratorio](./settings) — provider connections, project mapping, and Agent execution policy
