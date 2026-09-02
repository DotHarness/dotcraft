# Connect GitLab to Oratorio

Once GitLab is connected, its issues and merge requests sync into the Oratorio board, and the Agent's review notes and implementation branches can be written back. A project-scoped token carries the connection.

## Create a project token

Prefer a Project Access Token so its authority stays inside one project. Grant the access your enabled operations need, and nothing more:

| Operation | GitLab access |
| --- | --- |
| **Import issues and merge requests** | Read API access |
| **Read repository content** | Repository read access |
| **Publish notes and review status** | API access |
| **Deliver a merge request** | Repository write and API access |

Each connected project uses its own profile and token.

## Connect the project

1. Open the Oratorio Board and select **Connect GitLab**. The same flow starts from **Connect a source** in Oratorio settings.
2. Choose the token kind and paste the token. Keep the default endpoint for GitLab.com, or enter the root address of a self-managed instance.
3. Enter the project as `group/project`. Subgroups work too.
4. Choose the DotCraft workspace that holds the project's checkout.
5. Keep the sync schedule and automatic review defaults. Leave writes off until the first reviews look right.
6. Select **Connect and sync**. Oratorio saves the configuration, runs a first sync, and confirms read access.

Add more projects by running the flow again. Private projects need no extra setup. Oratorio fetches review targets into the mapped checkout with the project token, so that checkout doesn't need stored Git credentials of its own.

## Enable webhook delivery

Webhooks are optional, but they make source changes appear on the board sooner. Set the project webhook URL to:

```text
https://your-oratorio-host/api/v1/sources/gitlab/webhook
```

Save the same webhook secret or signing token in the GitLab project profile, then enable issue, merge request, and note events. Keep the endpoint private unless your [deployment](../self-hosted/server-deployment) provides an authenticated ingress boundary.

A local-only Desktop normally can't receive GitLab cloud webhooks. Use manual or scheduled sync when no reachable endpoint exists.

## Related docs

- [Follow the Oratorio workflow](./workflow) — how synced work moves through the Board
- [Configure Oratorio](./settings) — tune review automation, worktrees, and delivery
