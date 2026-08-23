# Connect GitLab to Oratorio

Connect a GitLab project to synchronize issues and merge requests, publish feedback, and deliver implementation work.

## Create a project token

Prefer a Project Access Token so its authority stays limited to one project. Grant only the access required for the operations you enable:

| Operation | GitLab access |
| --- | --- |
| **Import issues and merge requests** | Read API access |
| **Read repository content** | Repository read access |
| **Publish notes and review status** | API access |
| **Deliver a merge request** | Repository write and API access |

Each configured project has its own profile and token.

## Configure the provider

1. Open the Oratorio Board, select **Oratorio settings**, then choose **GitLab**.
2. Enable source reads. Keep the GitLab.com endpoint, or enter the root address of a self-managed GitLab instance.
3. Add a project profile with its instance and full `group/project` path. Subgroups are supported.
4. Add the project token to that profile.
5. Return to Oratorio settings and add the project with its matching DotCraft workspace.
6. Enable **Source writes** only when Oratorio should publish notes, status, branches, or merge requests.
7. Select **Sync now** and confirm that the project reports read access.

Private projects need no further setup. Oratorio fetches review targets into the mapped checkout with the project profile token, so the checkout itself does not need stored Git credentials. Git credentials configured on the host are deliberately ignored.

## Enable webhook delivery

Webhooks are optional and reduce the delay before source changes appear. Configure the project webhook URL as:

```text
https://your-oratorio-host/api/v1/sources/gitlab/webhook
```

Save the same webhook secret or signing token in the GitLab project profile, then enable issue, merge request, and note events. Keep the endpoint private unless your deployment provides an authenticated ingress boundary.

A local-only Desktop session normally cannot receive GitLab cloud webhooks. Use manual or scheduled sync when no reachable endpoint is available.

## Related docs

- [Oratorio](../oratorio)
- [Follow the Oratorio workflow](./workflow)
- [Configure Oratorio](./settings)
- [Deploy the DotCraft Stack](../self-hosted/server-deployment)

