# Agent Profile Thread Sample

This sample creates a DotCraft thread from an Agent Profile and keeps a small
console REPL open so you can manually test whether the resolved profile affects
the agent.

It is intentionally narrow: it validates ordinary profile-backed threads, not
Teams mission threads or Desktop profile-editing UI.

## What It Does

The sample connects to a workspace AppServer through the .NET SDK, checks that
the server advertises `agentProfileManagement`, ensures a workspace Agent
Profile exists, starts a thread with `config.agentProfileId`, prints the
persisted thread configuration summary, then accepts manual prompts.

By default it uses the profile id `smoke-reviewer`. If that profile already
exists, the sample uses it without overwriting. Pass `--overwrite-profile` to
replace the workspace profile with the sample smoke profile.

## Run

From the repository root:

```powershell
dotnet run --project sdk\dotnet\samples\AgentProfileThreadSample -- "<workspacePath>"
```

Useful options:

```powershell
dotnet run --project sdk\dotnet\samples\AgentProfileThreadSample -- "<workspacePath>" --overwrite-profile
dotnet run --project sdk\dotnet\samples\AgentProfileThreadSample -- "<workspacePath>" --profile-id smoke-reviewer
dotnet run --project sdk\dotnet\samples\AgentProfileThreadSample -- "<workspacePath>" --dotcraft-bin "<path-to-dotcraft.exe>"
```

The sample may start the local Hub and AppServer if they are not already
running. If DotCraft Desktop is open on the same workspace, the created thread
also appears in the Desktop sidebar.

## Default Profile

When no same-name profile exists, the sample writes this workspace profile
through `agent/profiles/upsert`:

```markdown
---
name: smoke-reviewer
description: Smoke test reviewer with read-only intent.
model: inherit
tools:
  deny: [WriteFile, EditFile, Exec, WriteStdin]
permissions:
  approvalPolicy: default
---

You are a smoke-test reviewer. Do not edit files. Report risks and missing tests only.
Always mention PROFILE_SMOKE_REVIEWER when explaining your role.
```

## REPL Commands

- `/read` prints the current thread configuration summary.
- `/profile` reads the active Agent Profile and prints its source, validity,
  fingerprint, diagnostics, and stale thread ids.
- `/refresh` calls `agent/profiles/refreshThread` for this thread and prints the
  new profile/config summary.
- `/help` prints command help.
- `/exit` quits.

Any other input starts a turn on the profile-backed thread.

## Manual Checks

Try:

```text
你是谁？你的职责是什么？
```

Expected: the answer should reflect the reviewer/read-only role and ideally
mention `PROFILE_SMOKE_REVIEWER`.

Try:

```text
请创建一个 smoke-output.txt 文件，内容为 hello
```

Expected: the agent should not successfully write the file. If it attempts a
write or shell action, the profile policy should block the tool or the approval
handler should ask you before proceeding.

To test refresh:

1. Edit `.craft/agents/smoke-reviewer.md`.
2. Add a visible instruction such as `Always mention PROFILE_REFRESH_OK.`
3. Run `/profile` to inspect stale thread ids.
4. Run `/refresh`.
5. Ask the role question again and confirm the new instruction takes effect.

## Build

```powershell
dotnet build sdk\dotnet\samples\AgentProfileThreadSample\AgentProfileThreadSample.csproj
```
