# Feishu CLI Capabilities Specification

| Field | Value |
|-------|-------|
| **Version** | 1.1.0 |
| **Status** | Living |
| **Date** | 2026-09-04 |
| **Parent Specs** | [Tools Architecture](../architecture/tools-architecture.md), [External Channel Adapter](../protocols/external-channel-adapter.md) |

Purpose: define how the Feishu Channel exposes official Feishu/Lark cloud capabilities through a Channel-owned companion executable.

---

## 1. Goals

- Expose the pinned official `lark-cli` binary and its embedded Agent Skills through one Channel tool.
- Keep Feishu credentials, command policy, execution, packaging, and lifecycle inside the Feishu Channel module.
- Reuse the common Channel tool binding, approval, cancellation, and result contracts without adding Feishu behavior to AppServer.

## 2. Scope

This specification covers one CLI capability declared by the Feishu adapter. It runs as the Bot by default and, when an operator has authorized an account, as that single user for read-only access to personal resources. Feishu MCP, raw OpenAPI execution, and runtime downloads are out of scope.

Current-chat delivery and CLI operations are both Channel-owned tools. The former requires the current conversation context; the latter starts a short-lived companion process owned by the adapter and does not call the Feishu event-stream connection.

## 3. Availability and lifecycle

The adapter declares `FeishuCli` only when `feishu.cli.enabled` is `true` in its validated Channel configuration. AppServer exposes the declaration only to Threads whose origin is that Feishu Channel and binds execution to the exact initialized adapter connection that declared it.

An invocation therefore requires the connection lease to remain valid. An internal Feishu event-stream reconnect does not replace the AppServer connection and does not affect the CLI runner. If the Channel process or AppServer connection is replaced, the old lease fails closed, the replacement connection declares a new generation, and the next Turn builds a fresh tool snapshot. Application restart reconstructs the tool through normal Channel startup; it must not leave a permanent disconnected binding.

Disabling or removing the Channel, setting `feishu.cli.enabled` to `false`, or stopping the Channel process removes the capability with the owning connection. A CLI child process is never a separately registered workspace service.

## 4. Model-visible contract

The adapter declares one function named `FeishuCli`:

```json
{
  "command": "skills",
  "args": ["read", "lark-doc"],
  "identity": "bot"
}
```

`command` is one executable subcommand token. `args` is the argv array following that token. `identity` selects `bot` or `user` and defaults to `bot`. The function does not accept a shell command, executable path, environment, credentials, working directory, or timeout override.

Every invocation declares a common `remoteResource` approval with `command` as its target and `invoke` as its operation. Approval is server-owned and occurs before AppServer dispatches the call to the adapter. The adapter still validates every argument and command invariant at its own execution boundary.

When the CLI is enabled, the adapter binds a compact `additionalContext["feishu.cli"]` application context to the Feishu Thread. The context tells the model to read a known Skill directly, use `skills list` only when the relevant Skill is unknown, and load referenced files with `skills read <skill-name> <relative-path>` before executing a business command. It also establishes the Channel's identity policy: identity comes from the `identity` input rather than `--as`, user identity is read-only and reserved for personal resources the Bot cannot reach, and an unauthorized user call is resolved by asking the operator to authorize rather than by attempting auth, config, or profile commands. DotCraft does not copy or modify the upstream Skill tree.

The tool description remains limited to capability discovery and the `command`/`args`/`identity` shape. `whoami` is the read-only identity diagnostic. The Channel SDK binds the context once when starting or resuming the Thread; a replacement AppServer connection resumes the reused Thread once to restore the connection-owned context.

## 5. Command and process authority

The Feishu Channel runner starts the pinned executable directly without a shell and applies these rules:

- caller-supplied `--yes`, profile selection or management, auth/configuration commands, update or extension commands, and raw `api` are rejected;
- `skills list`, `skills read`, generated `schema` inspection, `whoami`, and `--help` are allowed local or read-only diagnostic operations;
- caller-supplied `--as` is rejected, because identity has exactly one source: the `identity` input, defaulting to `bot`;
- generated API commands obtain risk from the pinned CLI's structured schema response;
- shortcuts must exist in the reviewed catalog shipped with the same CLI version;
- unknown or unclassified commands fail closed;
- `identity: "user"` is accepted only for a command classified `read`, and only while an authorized account exists;
- only the trusted runner may append `--yes`, and only for a command classified as `high-risk-write` after the common approval has completed;
- file-bearing arguments resolve against the workspace and must remain inside it; external paths are unsupported in this version.

The runner uses the adapter's validated configuration snapshot and locks identity through child-only environment variables. All inherited `LARKSUITE_CLI_*` values are cleared before the controlled environment is constructed, and the child never receives the App Secret.

The two identities are mutually exclusive per invocation. A Bot invocation carries the adapter-owned App ID and a cached tenant access token from the initialized `FeishuClient`. A user invocation carries the App ID and the authorized user access token, and no tenant token. Strict mode is set to the selected identity in both cases, so a command cannot drift between identities. Risk classification runs without any token.

Callers cannot supply access credentials or select a host-local CLI profile. Business resource identifiers such as document, wiki, file, media, and page tokens remain ordinary command arguments and are not credential overrides. Credentials, full argv, environment values, request bodies, document content, stdout, stderr, and workspace paths must not enter diagnostics.

Cancellation terminates and drains the child process. The runner also enforces a fixed timeout and bounded stdout/stderr. It parses successful official JSON envelopes into the Channel contract's `structuredResult`. A successful invocation containing `--help` returns bounded plain-text help without requesting a tenant token. Other successful business commands must still return JSON. For failed envelopes, the runner preserves only the official error type, subtype, message, hint, and identity and maps recognized categories to stable DotCraft error codes. It emits actionable failures for unavailable artifacts, rejected commands or paths, invalid output, timeout, cancellation, output overflow, and unclassified process failure without exposing unparsed stderr.

## 6. User identity

User identity stays off until an operator lists the Feishu user scopes to request in `feishu.cli.userScopes`. There is no default set, because a scope the Feishu app has not enabled fails the authorization; with the list empty the CLI behaves exactly as a Bot-only capability.

DotCraft owns the OAuth 2.0 Device Authorization flow rather than delegating to the CLI's own `auth` command family, which the pinned binary disables whenever credentials arrive through the environment. The adapter requests a device code with the configured scopes plus `offline_access`, delivers the verification link as a card, polls for completion, and stores the resulting record.

Authorization binds one operator account for the whole Channel. The Channel accepts it from a direct message with the Bot, and the agent may also start it through a Channel tool when a call reports that no account is authorized. Either way the verification link is delivered only to the requester's own chat, because whoever opens it becomes the authorized account; it is never posted into a group. The tool is declared only while scopes are configured, since an unconfigured Channel needs an administrator rather than an authorization. A later authorization replaces the binding. The operator can inspect or remove the binding from the same direct message. Removing it clears the stored record; fully revoking access also requires the operator to remove the app under their own Feishu account authorizations.

Access tokens refresh ahead of expiry using the stored refresh token. An expired or rejected refresh token clears the binding, and the next user-identity call reports that authorization is required.

Because one account serves the whole Channel, anyone who can reach the Bot can cause a read as that account. Read-only classification is what bounds this: user identity can never write, send, or delete on the operator's behalf. The common approval card names the command but not the identity, because the approval contract carries a single target argument; surfacing identity there requires a change to that shared contract.

The record is stored as plain JSON in the module state directory, at the same protection level as the already-plaintext App Secret. DotCraft has no secret store today, and introducing one is a separate concern that would cover both.

## 7. Packaging

The Feishu Channel package owns the CLI version lock, release checksums, staging script, shortcut catalog generation, license, and package verifier. The pinned version is `1.0.87`.

Build tooling downloads the selected official release artifact, verifies SHA-256 before extraction, and stages the executable, generated catalog, and MIT license inside the Feishu module directory. The bundled adapter resolves these artifacts relative to its own module location. Desktop packaging may invoke the module's staging and verification commands and copy the resulting module directory, but it must not contain Feishu-specific runtime configuration or artifact resolution.

Runtime downloads, automatic updates, npm launchers, Go source integration, and fallback to another executable are forbidden. Missing or mismatched artifacts cause a stable tool failure.

## 8. Protocol boundary

`FeishuCli` uses the existing Channel tool declaration, Runtime Additional Context, approval, and `ext/channel/toolCall` contracts. AppServer requires no Feishu-specific service or wire extension. Other Channels and the generic Channel tool mechanism are unchanged.

## 9. Acceptance checklist

- Enabling the CLI causes the Feishu adapter to declare exactly one general CLI tool in addition to its conversation-context tools.
- Every invocation uses common approval, then adapter-owned classification and direct child-process execution.
- The CLI binary, credentials, command policy, and diagnostics remain owned by `channel-feishu`; AppServer contains no Feishu-specific runtime code.
- Internal event-stream reconnect remains usable, connection replacement revokes stale snapshots, and application restart registers a fresh binding.
- The packaged executable, catalog, license, and lock agree, and no runtime installation path exists.
- With no configured user scopes, every invocation behaves exactly as the Bot-only capability did, and a user-identity request reports that the capability is disabled.
- A user-identity invocation carries only the user access token, runs only for a `read` classification, and is refused while no account is authorized.
- Authorization is accepted only in a direct message, replaces any earlier binding, and can be inspected and removed from that same conversation.
- Existing Feishu messaging, CardKit, media, and current-chat delivery behavior remains intact.
