# Connect GitHub to Oratorio

Use a GitHub App to synchronize issues and pull requests, publish review feedback, and deliver implementation work.

## Create the GitHub App

Create or reuse a GitHub App under the user or organization that owns the repositories. Install it only on repositories Oratorio should access.

Grant the smallest permissions required for the enabled operations:

| Operation | GitHub permission |
| --- | --- |
| **Import issues and pull requests** | Issues and pull requests: read |
| **Read files and discussion** | Pull requests: read; contents: read |
| **Publish comments and reviews** | Issues and pull requests: write |
| **Publish the review check** | Checks: write |
| **Deliver a pull request** | Contents and pull requests: write |

Generate a private key for the App. Keep the App ID and key available while configuring DotCraft.

## Configure the provider

1. Open the Oratorio Board, select **Oratorio settings**, then choose **GitHub**.
2. Keep the default endpoint for GitHub.com, or enter the API endpoint for GitHub Enterprise.
3. Enter the App ID and add the private key or private-key path.
4. Add an installation profile for each GitHub owner. Oratorio can detect the Installation ID after the project route is saved, or you can enter it manually.
5. Return to Oratorio settings and add each repository with its matching DotCraft workspace.
6. Enable **Source writes** only when Oratorio should publish comments, reviews, checks, branches, or pull requests.
7. Select **Sync now** and confirm that the repository reports read access.

Private repositories need no further setup. Oratorio fetches review targets into the mapped checkout with the App installation credentials, so the checkout itself does not need stored Git credentials.

## Enable webhook delivery

Webhooks are optional for synchronization and required for the GitHub comment command. A local-only Desktop session normally cannot receive GitHub cloud webhooks; manual and scheduled sync still work.

For a remote DotCraft Stack, expose only the restricted webhook endpoint:

```bash
dotcraft stack webhook enable \
  --dir /opt/dotcraft-stack \
  --public-host hooks.example.com
```

Set the GitHub App webhook URL to the endpoint printed by the command, paste the generated secret into the App, keep SSL verification enabled, and subscribe to issue comments plus the issue, pull request, review, and review-comment events used by your workflow.

An authorized repository collaborator can request a review on an open configured pull request with a comment containing only:

```text
@dotcraft-ai review
```

Add a one-time focus after the command when needed, for example `@dotcraft-ai review for security regressions`.

## Related docs

- [Oratorio](../oratorio)
- [Follow the Oratorio workflow](./workflow)
- [Configure Oratorio](./settings)
- [Deploy the DotCraft Stack](../self-hosted/server-deployment)

