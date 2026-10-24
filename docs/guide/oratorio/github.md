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

Add more repositories by running the flow again. Private repositories need no extra setup. Oratorio fetches review targets into the mapped checkout with the App installation credentials, so that checkout doesn't need stored Git credentials of its own.

## Ask Oratorio from a pull request

Owners, members, and collaborators of a connected repository can address Oratorio on any open pull request. Start a pull request comment, an inline review comment, or a reply in a review thread with `@dotcraft-ai`.

To request a review of the current head, post:

```text
@dotcraft-ai review
```

To point one review at a specific concern, use `/review` and add the concern on the same line, for example `@dotcraft-ai /review security regressions`.

Anything else after the mention is a question:

```text
@dotcraft-ai Why does the refresh path keep the old token?
```

Oratorio reacts with 👀 when it accepts a comment, then answers where you asked: in the pull request conversation, or in the review thread for an inline comment. A question never starts a review or changes the task.

To push back on one of Oratorio's findings, reply `@dotcraft-ai` in that finding's review thread. If the discussion shows the finding doesn't apply or is already handled, Oratorio dismisses it and resolves the thread, and still replies to you.

No webhook is needed. Oratorio picks up new comments within about 30 seconds, and it ignores comments posted before the repository was connected. Reactions and replies appear on GitHub once writes are on.

## Enable webhook delivery

Webhooks are optional. Sync and `@dotcraft-ai` comments work without them, and a webhook only gets events to Oratorio sooner. A local-only Desktop normally can't receive GitHub cloud webhooks.

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
