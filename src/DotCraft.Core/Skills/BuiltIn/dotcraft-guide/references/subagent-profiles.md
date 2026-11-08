# Custom Subagent Profiles

A custom subagent profile lets DotCraft hand tasks to another command-line agent. Each task launches the CLI once, passes the task in, and reads its final answer back. The user describes the CLI; you work out the launch details.

Field reference: `/configuration/subagents` on the documentation site, section "External CLI profiles". `dotcraft config schema --json --section SubAgentProfiles` is the authority for field names.

## Steps

1. **Find the binary.** Confirm the CLI is on `PATH` with `Exec`. If it is not, ask for the absolute path. Do not install it.
2. **Read its help.** Run `<bin> --help`, and the help of any `exec`, `run`, or `-p` subcommand it lists. Find the non-interactive mode: the flags that make it answer one prompt and exit without asking anything.
3. **Decide each part from the help, not from memory.**
   - Task input, `inputMode`: `arg` appends the task as the last argument; `arg-template` uses `inputArgTemplate` with `{task}`, for a flag such as `--prompt {task}`; `stdin` pipes it; `env` sets `inputEnvKey`.
   - Fixed flags, `args`: the subcommand and flags for non-interactive mode, one array entry per argument.
   - Final answer: plain stdout is `outputFormat: "text"`. When the CLI can print JSON, prefer `"json"` with `outputJsonPath` pointing at the answer, such as `result`. When it can write the last message to a file, use `outputFileArgTemplate` with `{path}` and `readOutputFile: true`.
   - Token counts, optional: `outputInputTokensJsonPath` and `outputOutputTokensJsonPath` when the JSON reports them.
   - Approvals, `permissionModeMapping`: keys `interactive`, `auto-approve`, `restricted`, each the CLI flags for that level, such as a read-only sandbox for `restricted`. Leave out what the CLI has no flag for.
   - Resume, optional: only when the CLI prints a session id and accepts it back. Set `supportsResume: true`, `resumeArgTemplate` with `{sessionId}`, and `resumeSessionIdJsonPath` or `resumeSessionIdRegex`. Without all three, leave resume off.
   - Credentials: copy the variable names the CLI needs with `envPassthrough`. Never write a key into `env`.
4. **Try the command.** Run the exact command line the profile describes once through `Exec`, with a small read-only task such as "Reply with the word ready." Check that the answer comes out where the profile reads it. Fix the profile and try again before writing it.
5. **Write it.** Add the profile under `SubAgentProfiles` in `<workspace>/.craft/config.json`, keyed by a short lowercase name. Write only the fields you decided; the rest default. Keep every other key in the file as it is. To change an existing profile, edit only its entry.
6. **Hand back.** Tell the user the profile name and what it runs. It appears in Desktop > Settings > Subagents, where they can turn it on or off and remove it. It takes effect when that page next opens; no restart.

## Example

```json
{
  "SubAgentProfiles": {
    "my-cli": {
      "runtime": "cli-oneshot",
      "bin": "my-cli",
      "args": ["run", "--non-interactive"],
      "inputMode": "arg",
      "outputFormat": "json",
      "outputJsonPath": "result",
      "envPassthrough": ["MY_CLI_API_KEY"],
      "permissionModeMapping": {
        "interactive": "--sandbox read-only",
        "auto-approve": "--yes",
        "restricted": "--sandbox read-only"
      },
      "timeout": 600
    }
  }
}
```

`native`, `codex-cli`, and `cursor-cli` are built in. Use a different name unless the user asks to override one.
