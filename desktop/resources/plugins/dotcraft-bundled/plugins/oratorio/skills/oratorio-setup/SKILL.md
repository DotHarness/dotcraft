---
name: oratorio-setup
description: Use when the user wants to connect a GitHub or GitLab repository to Oratorio, or change how Oratorio syncs, reviews, or routes a connected repository.
---

# Oratorio Setup

Connect a repository so its issues and pull or merge requests appear on the Oratorio board and get agent reviews. Use the Oratorio tools `GetSourceSetup`, `ConnectProject`, `RequestSourceCredential`, and `SyncProject`. DotCraft assigns their namespace, so search for them by name.

If these tools are unavailable, tell the user to connect the Oratorio app in Plugins › Oratorio and start a new conversation, then stop.

## Workflow

1. Call `GetSourceSetup`. Reuse what is already configured, such as endpoints, the GitHub App, and workspaces. If `writable` is false, explain `disabledReason` and stop.
2. Identify the repository. When the user does not name it, read the current workspace's remote with `git remote get-url origin` and derive the provider, host, and project path. For a self-managed GitLab, the endpoint is `https://<host>`. For GitHub Enterprise, it is the API endpoint `https://<host>/api/v3`.
3. Pick the workspace that holds the repository's checkout, normally the current one. It must be a path on the Oratorio server, so prefer a matching entry from `workspaces`.
4. Confirm the settings in one short message, asking only about what the user has not said. Defaults: sync every 15 minutes, automatic review of new pull or merge requests on, writes off. Writes let Oratorio publish reviews, comments, and changes back to the provider, and they apply to every repository of that provider.
5. For GitHub, the owner needs a GitHub App installed on the repository. If none is configured, walk the user through creating one with the permissions in the [GitHub guide](https://www.dotcraft.net/guide/oratorio/github), then ask for the App ID. If the private key file is on the Oratorio server, pass its path as `githubPrivateKeyPath`.
6. Call `ConnectProject`. It saves immediately, updates an existing project in place, and runs a first sync when credentials are ready. Settings you omit keep their current values.
7. When credentials are missing, call `RequestSourceCredential` and wait. The user enters the token or private key in the card, which stores it without passing it to you. Never ask for a secret in the conversation. If the user pastes one anyway, do not repeat it, and suggest rotating it. For GitLab, recommend a project access token with the access listed in the [GitLab guide](https://www.dotcraft.net/guide/oratorio/gitlab).
8. After the user says the credential is saved, call `ConnectProject` again with the same arguments. It turns on the schedule and runs the first sync.
9. Report the project, workspace, schedule, review and write settings, and what the sync imported. When a sync fails, explain the error and the fix, such as a missing token scope or an App that is not installed on the owner.

To change a connected project, call `ConnectProject` with its workspace and only the settings that change. Use `SyncProject` when the user wants a connected project synced now. Removing a project, webhooks, and runtime limits stay in Oratorio Settings.
