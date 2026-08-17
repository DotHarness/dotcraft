# Feishu CLI Capabilities Specification

| Field | Value |
|-------|-------|
| **Version** | 1.0.2 |
| **Status** | Living |
| **Date** | 2026-08-17 |
| **Parent Specs** | [Tools Architecture](../architecture/tools-architecture.md), [External Channel Adapter](../protocols/external-channel-adapter.md) |

Purpose: define how the Feishu Channel exposes official Feishu/Lark cloud capabilities through a Channel-owned companion executable.

---

## 1. Goals

- Expose the pinned official `lark-cli` binary and its embedded Agent Skills through one Channel tool.
- Keep Feishu credentials, command policy, execution, packaging, and lifecycle inside the Feishu Channel module.
- Reuse the common Channel tool binding, approval, cancellation, and result contracts without adding Feishu behavior to AppServer.

## 2. Scope

This specification covers one Bot-identity CLI capability declared by the Feishu adapter. User OAuth identity, Feishu MCP, raw OpenAPI execution, and runtime downloads are out of scope.

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
  "args": ["read", "lark-doc"]
}
```

`command` is one executable subcommand token. `args` is the argv array following that token. The function does not accept a shell command, executable path, environment, credentials, working directory, or timeout override.

Every invocation declares a common `remoteResource` approval with `command` as its target and `invoke` as its operation. Approval is server-owned and occurs before AppServer dispatches the call to the adapter. The adapter still validates every argument and command invariant at its own execution boundary.

When the CLI is enabled, the adapter binds a compact `additionalContext["feishu.cli"]` application context to the Feishu Thread. The context tells the model to read a known Skill directly, use `skills list` only when the relevant Skill is unknown, and load referenced files with `skills read <skill-name> <relative-path>` before executing a business command. It also establishes that the Channel's Bot-only identity policy takes precedence over generic upstream recommendations to use user identity. DotCraft does not copy or modify the upstream Skill tree.

The tool description remains limited to capability discovery and the `command`/`args` shape. `whoami` is the read-only identity diagnostic. The Channel SDK binds the context once when starting or resuming the Thread; a replacement AppServer connection resumes the reused Thread once to restore the connection-owned context.

## 5. Command and process authority

The Feishu Channel runner starts the pinned executable directly without a shell and applies these rules:

- caller-supplied `--yes`, profile selection or management, auth/configuration commands, update or extension commands, and raw `api` are rejected;
- `skills list`, `skills read`, generated `schema` inspection, `whoami`, and `--help` are allowed local or read-only diagnostic operations;
- `--as bot` may be passed through, while the pinned CLI's forced Bot strict mode rejects incompatible identities such as `--as user`;
- generated API commands obtain risk from the pinned CLI's structured schema response;
- shortcuts must exist in the reviewed catalog shipped with the same CLI version;
- unknown or unclassified commands fail closed;
- only the trusted runner may append `--yes`, and only for a command classified as `high-risk-write` after the common approval has completed;
- file-bearing arguments resolve against the workspace and must remain inside it; external paths are unsupported in this version.

The runner uses the adapter's validated configuration snapshot and forces Bot identity through child-only environment variables. It obtains a cached tenant access token from the initialized `FeishuClient` and supplies that token to credential-requiring CLI invocations. The child receives the adapter-owned App ID and tenant access token, but not the App Secret. All inherited `LARKSUITE_CLI_*` values are cleared before the controlled environment is constructed.

Callers cannot supply access credentials or select a host-local CLI profile. Business resource identifiers such as document, wiki, file, media, and page tokens remain ordinary command arguments and are not credential overrides. Credentials, full argv, environment values, request bodies, document content, stdout, stderr, and workspace paths must not enter diagnostics.

Cancellation terminates and drains the child process. The runner also enforces a fixed timeout and bounded stdout/stderr. It parses successful official JSON envelopes into the Channel contract's `structuredResult`. A successful invocation containing `--help` returns bounded plain-text help without requesting a tenant token. Other successful business commands must still return JSON. For failed envelopes, the runner preserves only the official error type, subtype, message, hint, and identity and maps recognized categories to stable DotCraft error codes. It emits actionable failures for unavailable artifacts, rejected commands or paths, invalid output, timeout, cancellation, output overflow, and unclassified process failure without exposing unparsed stderr.

## 6. Packaging

The Feishu Channel package owns the CLI version lock, release checksums, staging script, shortcut catalog generation, license, and package verifier. The pinned version is `1.0.87`.

Build tooling downloads the selected official release artifact, verifies SHA-256 before extraction, and stages the executable, generated catalog, and MIT license inside the Feishu module directory. The bundled adapter resolves these artifacts relative to its own module location. Desktop packaging may invoke the module's staging and verification commands and copy the resulting module directory, but it must not contain Feishu-specific runtime configuration or artifact resolution.

Runtime downloads, automatic updates, npm launchers, Go source integration, and fallback to another executable are forbidden. Missing or mismatched artifacts cause a stable tool failure.

## 7. Protocol boundary

`FeishuCli` uses the existing Channel tool declaration, Runtime Additional Context, approval, and `ext/channel/toolCall` contracts. AppServer requires no Feishu-specific service or wire extension. Other Channels and the generic Channel tool mechanism are unchanged.

## 8. Acceptance checklist

- Enabling the CLI causes the Feishu adapter to declare exactly one general CLI tool in addition to its conversation-context tools.
- Every invocation uses common approval, then adapter-owned classification and direct child-process execution.
- The CLI binary, credentials, command policy, and diagnostics remain owned by `channel-feishu`; AppServer contains no Feishu-specific runtime code.
- Internal event-stream reconnect remains usable, connection replacement revokes stale snapshots, and application restart registers a fresh binding.
- The packaged executable, catalog, license, and lock agree, and no runtime installation path exists.
- Existing Feishu messaging, CardKit, media, and current-chat delivery behavior remains intact.
