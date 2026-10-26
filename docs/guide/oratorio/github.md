# Connect GitHub to Oratorio

Once GitHub is connected, its issues and pull requests sync into the Oratorio board, and the Agent's review comments, checks, and implementation branches can be written back. A GitHub App carries the connection.

## Create the GitHub App

Create a GitHub App under the user or organization that owns the repositories, or reuse an existing one. Install it only on the repositories Oratorio should reach.

Grant the permissions your enabled operations need, and nothing more:

| Operation | GitHub permission |
| --- | --- |
| **Import issues and pull requests** | Issues and pull requests: read |
| **Read files and discussion** | Pull requests: read, contents: read |
| **Publish comments and reviews** | Issues and pull requests: write |
| **Answer `@dotcraft-ai` comments** | Issues and pull requests: write |
| **Publish the review check** | Checks: write |
| **Deliver a pull request** | Contents and pull requests: write |

Then generate a private key for the App. You'll need the App ID and that key while configuring DotCraft.

## Connect the repository

1. Open the Oratorio Board and select **Connect GitHub**. The same flow starts from **Connect a source** in Oratorio settings.
2. Enter the App ID and the private key. Keep the default endpoint for GitHub.com. For GitHub Enterprise, enter your own API endpoint.
3. Enter the repository as `owner/repository`. Oratorio detects the App installation when you connect. Enter the installation ID yourself only if detection fails.
4. Choose the DotCraft workspace that holds the repository's checkout.
5. Keep the sync schedule and automatic review defaults. Leave writes off until the first reviews look right.
6. Select **Connect and sync**. Oratorio saves the configuration, runs a first sync, and confirms read access.

Public and private repositories are supported. To add more repositories, repeat these steps.

## Ask Oratorio from GitHub

Owners, members, and collaborators of a connected repository can ask questions on open issues and pull requests. Enable [writes](./settings), then start a new comment with `@dotcraft-ai`, followed by your question.

Oratorio reacts with 👀 when it accepts a comment, then replies where you asked. No webhook is needed: new comments are usually picked up within about 30 seconds. Comments posted before the repository was connected are ignored.

### Discuss an issue

To investigate a problem or discuss a solution, post an issue comment:

```text
@dotcraft-ai What changes would address this issue?
```

Oratorio uses the issue body, existing discussion, and repository code to answer. A question doesn't start code changes or create a pull request.

### Review and discuss a pull request

To review the latest code in a pull request, post a comment:

```text
@dotcraft-ai review
```

To specify a focus, use `/review` and add it on the same line, for example `@dotcraft-ai /review security regressions`. Review commands apply only to pull requests.

To ask about code or review findings, write your question directly:

```text
@dotcraft-ai Why does the refresh path keep the old token?
```

You can also ask in inline review comments and review threads. Oratorio replies in the same thread. A question doesn't start another review.

To challenge a finding, explain your reasoning and ask a question in its review thread. If the discussion confirms the finding doesn't apply or is already handled, Oratorio dismisses it and resolves the thread.

## Enable webhook delivery

Webhooks can speed up sync and comment responses. A local-only Desktop normally can't receive GitHub cloud webhooks, so no setup is needed.

If you run a remote [DotCraft Stack](../../self-hosting/server-deployment), expose only the restricted webhook endpoint:

```bash
dotcraft stack webhook enable \
  --dir /opt/dotcraft-stack \
  --public-host hooks.example.com
```

Set the GitHub App webhook URL to the endpoint the command prints, paste the generated secret into the App, keep SSL verification enabled, then subscribe to the issue comment, issue, pull request, review, and review comment events your workflow uses.

## Related docs

- [Follow the Oratorio workflow](./workflow) — how synced work moves through the Board
- [Configure Oratorio](./settings) — tune review automation, worktrees, and delivery
