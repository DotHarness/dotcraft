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

## Enable webhook delivery

Sync doesn't depend on webhooks, but the GitHub comment command does. A local-only Desktop normally can't receive GitHub cloud webhooks, and manual and scheduled sync still work.

If you run a remote [DotCraft Stack](../self-hosted/server-deployment), expose only the restricted webhook endpoint:

```bash
dotcraft stack webhook enable \
  --dir /opt/dotcraft-stack \
  --public-host hooks.example.com
```

Set the GitHub App webhook URL to the endpoint the command prints, paste the generated secret into the App, keep SSL verification enabled, then subscribe to the issue comment, issue, pull request, review, and review comment events your workflow uses.

After that, anyone with collaborator access can request a review on a connected, still-open pull request by posting a comment containing only:

```text
@dotcraft-ai review
```

To point one review at a specific concern, add it after the command, for example `@dotcraft-ai review for security regressions`.

## Related docs

- [Follow the Oratorio workflow](./workflow) — how synced work moves through the Board
- [Configure Oratorio](./settings) — tune review automation, worktrees, and delivery
