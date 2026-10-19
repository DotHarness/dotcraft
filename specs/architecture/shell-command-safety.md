# Shell Command Safety

| Field | Value |
|---|---|
| Version | 0.9.1 |
| Status | Living |
| Date | 2026-10-19 |
| Owner | DotCraft.Core |

## 1. Purpose

This specification defines how DotCraft decides whether a shell command issued through the `Exec` tool runs, prompts for approval, or is rejected, how that decision is bound to the executable that will run the command, and what an approval remembers.

One kernel makes the decision: resolve the shell identity, lower the script into argument vectors, match policy rules, fall back to danger and launch-directory checks, aggregate, then approve against a structured key. The same kernel classifies read-only commands for Plan mode and read-only SubAgent roles.

### 1.1 Ownership

- This document owns shell identity, lowering, dangerous-command detection, policy rules, decision aggregation, the approval key, approval memory, and the execution gate.
- [Tool Architecture](tools-architecture.md) owns the dispatch pipeline and `Exec`/`WriteStdin` tool semantics; the shell gate is the runtime-stage authority for `Exec`.
- [Session Core](session-core.md) owns approval items and turn integration; the shell gate supplies the structured request in Section 9.
- Callers supply the effective role policy; Section 11 defines the meaning of `readOnly` shell access.

## 2. Non-goals

- Process isolation. The kernel reasons about command text; it does not confine what an approved process can touch.
- Proving that an interpreter argument is harmless. `python -c`, `node -e`, and similar arguments are code; the kernel treats them as opaque words.
- Parsing `cmd.exe` scripts. Cmd scripts are opaque and only scanned for dangerous literals.
- Inferring filesystem access or the current directory from command text. Shell processes run with host permissions and can access paths outside the workspace; file-tool guards do not isolate shell processes.

## 3. Terminology

| Term | Meaning |
|---|---|
| Shell identity | The kind and resolved absolute path of the shell executable that will run the script. |
| Script | The `command` text the model supplies. |
| Lowering | Converting a script into a list of argument vectors, one per simple command. |
| Plain lowering | Lowering that succeeded with every word literal; its result may prove properties of the command. |
| Literal extraction | Best-effort collection of literal words from every command in a script, including complex ones; may only raise risk, never prove safety. |
| Opaque | A script whose plain lowering failed. |
| Decision | `Allow < Prompt < Forbidden`; the ordering is the severity order. |
| Prefix rule | An ordered token prefix with a decision, matched against a lowered command. |
| Approval key | The structured identity of an approved execution. |

## 4. Shell identity

The `Exec` tool takes the script text and an optional `shell` selector; the host chooses and launches the shell. The selector resolves to a shell identity (kind and executable path), and the gate launches exactly that executable.

| Platform | Selector | Resolution |
|---|---|---|
| Windows | empty | First existing executable: `pwsh.exe` on `PATH`, `%ProgramFiles%\PowerShell\7\pwsh.exe`, `powershell.exe` on `PATH`, system Windows PowerShell, system cmd |
| Windows | `powershell`, `powershell.exe` | `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe` |
| Windows | `pwsh`, `pwsh.exe` | `PATH` lookup, then `%ProgramFiles%\PowerShell\7\pwsh.exe`; must resolve |
| Windows | `cmd`, `cmd.exe` | `%SystemRoot%\System32\cmd.exe` |
| Unix | empty | `/bin/bash` |
| Unix | `bash`, `sh`, `zsh`, `pwsh`, or a path whose file name is one of them | `PATH` lookup or the given absolute path; must exist |

Any other selector is `Forbidden` with a reason naming the selector. An unknown selector is never treated as the default shell, a bare name is never launched, and an explicit selector never falls back to another shell kind.

Shell kinds are `PowerShell`, `Pwsh`, `Cmd`, `Bash`, `Sh`, and `Zsh`, grouped into the `PowerShell`, `Cmd`, and `Posix` families; the family selects the lowerer.

Lifecycle hooks resolve their shell the same way, so hook commands and tool commands agree about what `pwsh` means. Approval and execution use the same resolved identity, and an existing terminal retains its launch identity. Host environment context reports the resolved default shell name and executable path; container context reports Bash (`/bin/bash`). Integrated client terminals on Windows use the same PowerShell discovery order, falling back to `COMSPEC` or `cmd.exe` only when no PowerShell executable is found.

## 5. Lowering

Lowering a script produces its family, the plain command list (present only when plain lowering succeeded), the first reason plain lowering failed, and the literal command list (equal to the plain list when plain).

Plain lowering fails closed: any construct not listed as accepted makes the script opaque. Lowerers never guess the runtime meaning of a construct they do not model.

### 5.1 PowerShell family

The lowerer parses with the PowerShell language parser in process; no PowerShell process starts during lowering.

Plain lowering accepts a script only when all of the following hold:

- The parser reports no errors.
- Every AST node is one of `ScriptBlockAst`, `NamedBlockAst`, `PipelineChainAst`, `PipelineAst`, `CommandAst`, `StringConstantExpressionAst`, `CommandParameterAst`.
- Script requirements, `using` statements, `param`, `dynamicparam`, `begin`, `process`, `clean` blocks, and traps in the end block are all absent. These regions execute code that the statement list does not show.
- Every command uses the plain invocation form (no `&` or `.` operator) and has no redirections.
- No word is the stop-parsing token `--%`.

Words are the parser's constant values, so escapes and Unicode quote or dash aliases carry their real runtime value. A parameter lowers to `-Name`; a parameter with an attached argument (`-Name:value`) lowers to `-Name:` followed by the argument value. A numeric literal is accepted only when its runtime value is spelled exactly as written (`100`, not `01`, `0x10`, `1kb`, or `1e3`).

Literal extraction walks every command in the tree, including those nested in script blocks and expressions, and collects its constant words in order.

Pipeline chains (`&&`, `||`) are accepted even though Windows PowerShell 5.1 rejects them at parse time; a script the launched shell cannot parse does not run.

### 5.2 Posix family

Plain lowering accepts scripts made only of simple commands joined by `&&`, `||`, `;`, `|`, and newlines. A simple command is a sequence of words where each word is one of:

- a bare word containing none of `{ } * ? [ ] \ ~ ^ # $ `` ` `` and not starting with `=`;
- a single-quoted string;
- a double-quoted string containing no `$`, no backtick, and no backslash escape;
- a concatenation of the above.

Everything else is opaque: variable assignments, redirections, `&`, subshells, groups, control flow, here-documents, comments, command or process substitution, and any unbalanced quote or empty command position.

Literal extraction tokenizes the whole script with quote awareness, splits at every separator (`; | & && || newline ( ) { }`) and at the keywords `then`, `do`, `else`, and keeps the literal words of each segment. Words with unresolved expansion are dropped rather than guessed.

### 5.3 Cmd family

Cmd scripts are always opaque. Literal extraction splits the script on `& && | ||` and tokenizes each segment with `cmd`-style quoting for dangerous-command detection only.

## 6. Dangerous commands

Dangerous-command detection inspects lowered commands and reports a match kind of `ForcedRemove` or `Other`.

| Family | Rule |
|---|---|
| Posix | `rm` with `-f` in any single-dash flag group or `--force`, scanning stops at `--` |
| Posix | `sudo <command>`: inspect the wrapped command |
| Posix | `env [-i \| --ignore-environment \| NAME=value]... [--] <command>`: inspect the wrapped command |
| Posix | `trap '<action>' ...`: inspect the action as a shell script |
| PowerShell | a delete cmdlet (`Remove-Item`, `ri`, `rm`, `del`, `erase`, `rd`, `rmdir`) and `-Force` in the same command |
| PowerShell | `Start-Process`, `start`, `saps`, `Invoke-Item`, `ii`, `explorer`, `mshta`, `rundll32 url.dll,FileProtocolHandler`, or a browser executable with an `http` or `https` URL argument |
| Cmd | `del` or `erase` with `/f`; `rd` or `rmdir` with both `/s` and `/q`; `start` with an `http` or `https` URL |

Executable names are compared by file name; on Windows the comparison is case-insensitive and ignores `.exe`, `.cmd`, `.bat`, and `.com`. Wrapper inspection is bounded to a depth of 8; deeper nesting is reported as dangerous.

Detection runs over literal extraction, so a dangerous literal inside a loop, a conditional, or a substitution is found even when the script is opaque.

## 7. Policy

### 7.1 Rules

A prefix rule is `{ prefix: string[], decision: allow | prompt | forbidden, justification?: string }`. It matches a lowered command when the command has at least as many words as the prefix and each prefix word equals the corresponding command word (ordinal; case-insensitive on Windows for the first word).

Matching looks up rules by the command's first word; when none match and the first word is a path, it retries with the file name (Windows extensions stripped). All matching rules apply and the most severe decision wins, so a broad `prompt` rule is not overridden by a narrower `allow` rule.

Rules come from the effective layered configuration value of `Tools.Shell.Policy.Rules` followed by the learned rules in `.craft/security/shell-rules.json`; equal rules are kept once. The effective rule set has a fingerprint (hash of the sorted rule texts) that participates in the approval key.

### 7.2 Banned prefixes

Some prefixes are too broad to remember as `allow` rules: shells and interpreters with inline-code flags (`bash -c`, `sh`, `zsh`, `cmd /c`, `powershell -Command`, `pwsh -c`, `python -c`, `node -e`, `perl -e`, `ruby -e`, `deno eval`, `bun -e`), wrappers (`env`, `sudo`, `xargs`, `nohup`, `timeout`), destructive verbs (`rm`, `del`, `rmdir`, `Remove-Item`), and version-control roots (`git`, `npm run`). A prefix exactly equal to a banned entry is never persisted; the kernel falls back to an exact approval key. Longer prefixes that merely start with a banned entry are allowed.

## 8. Evaluation

Evaluation produces an assessment: decision, reasons, risk, shell identity, lowering, rule matches, approval key, and what a remembering approval would store.

1. Resolve the shell identity. Failure is `Forbidden`.
2. Lower the script with the family lowerer.
3. Build the command list: the plain commands when plain; otherwise a single synthetic command `[<family sentinel>, <script>]` where the sentinel is `__shell_script__`, `__powershell_script__`, or `__cmd_script__`. An opaque script is never reduced to its first inner executable.
4. For each command, match rules. A rule match yields its decision and skips the fallback for that command.
5. For each unmatched command, apply the fallback:
   - a dangerous match yields `Prompt`, or `Forbidden` when the thread auto-approves prompts;
   - otherwise check the caller-supplied launch directory against the workspace roots. An outside directory yields `Prompt` when the thread requires approval outside the workspace and `Forbidden` otherwise. An inside directory yields `Allow`.
6. The assessment decision is the maximum over all commands.

Command arguments and script text are not scanned for paths, parent traversal, or directory changes; path blacklists apply to file tools, not shell command text. Launch-directory containment uses the same workspace boundary as file tools and resolves symbolic links before comparing. It controls where a process starts, not what it can access.

An `Allow` produced by an explicit `allow` rule bypasses danger detection for that command. Danger detection is part of the fallback, not a veto above rules.

## 9. Approval

### 9.1 Request

When the decision is `Prompt`, the gate raises a shell approval request carrying the raw script and resolved directory, the shell kind and executable, the plain commands (empty when opaque), the lowering outcome with its reject reason, the risk level (`dangerous`, `outsideWorkspace`, or `rule`) with reasons, the approval key, and what each remembering decision would store (Section 9.3).

The approval service receives the whole request with the ambient approval context. Decorators that route or label approvals carry their labels in the context and never rewrite the command text.

The approval request sent to clients carries `reasons`, `rememberedPrefixes`, and `remembersExactCommand`, so a client can state the scope of a remembering decision next to that option; the command text and directory travel as `operation` and `target`.

### 9.2 Approval key

The approval key consists of the shell kind and executable path, the working directory (full path, case-folded on Windows), the canonical command, and the policy fingerprint. The canonical command is the words of a plain single command, the words of a plain multi-command script joined with `&&` markers, or `[sentinel, script]` for an opaque script. The key hash is SHA-256 over the canonical JSON of these fields.

Two invocations share a key only when the same shell executable would run an identical canonical command in the same directory under the same rule set.

### 9.3 Memory

| Decision | Effect |
|---|---|
| `accept` | runs once |
| `acceptForSession` | stores the key hash in the thread's session scope; later requests with an equal key skip the prompt |
| `acceptAlways` | persists a rule or an exact key (below), and also applies to the session |
| `decline`, `cancel` | as defined by Session Core |

`acceptAlways` persists, per prompted command:

1. when a user `prompt` rule matched, an `allow` rule with that rule's prefix;
2. otherwise, when lowering is plain and the command was not flagged as dangerous, an `allow` rule whose prefix is the command's full word list;
3. otherwise, or when the prefix is banned, the exact approval key hash. A dangerous command is never widened into a rule by approval; only a configured rule can allow it unattended.

Learned rules are appended to `.craft/security/shell-rules.json`; exact keys are stored in `.craft/security/approvals.json` under a versioned `shell` section.

## 10. Execution gate

The shell execution gate runs after the invocation is recorded and before the process starts. It is the single enforcement point: every host-side `Exec`, wherever it runs, passes it.

- `Forbidden` returns a tool error carrying the reasons.
- `Prompt` goes to the approval service; a decline returns a tool error.
- `Allow`, or an approved prompt, hands the resolved shell identity to the terminal service, which launches only that executable. The executable that was assessed is the executable that runs.

The dispatcher's approval stage declares no approval for `Exec`; the gate owns it. The dispatcher's policy stage still enforces read-only classification (Section 11) and role or mode restrictions before the runtime stage.

### 10.1 Standard input

An admitted command can be an interpreter that keeps reading standard input, so `WriteStdin` is a second way into a process the gate already started. Because this specification does not confine what an approved process may touch, that input is the remaining place to decide, and the gate assesses it with the same kernel before it is written.

A running terminal keeps its resolved shell identity and immutable launch directory. Assessment uses that identity instead of resolving a selector, and the launch directory as approval context; it does not claim to describe the process's current directory.

One terminal admits one interaction at a time: assessment, approval, and the write are serialized per terminal. Terminals do not block each other.

Empty input polls for output and is not assessed. Input that is exactly the end-of-text character is not assessed. Terminal input a client sends on the person's behalf does not pass the gate; the person typing it is the authority.

An approval raised for standard input carries the input as its command and the launch directory as its working directory, and states that the text is being written to a running terminal whose own directory and state may since have moved.

## 11. Read-only classification

Read-only classification decides whether a command is a non-mutating observation. It is used by Plan mode and by SubAgent roles with `readOnly` shell access. Read-only is a property of the command text under every supported shell, so it does not depend on the host shell:

1. Any shell selector is denied; the selector changes which executable runs.
2. The script must lower plainly under both the Posix and the PowerShell lowerer. The Posix reject reason is reported first because it names the offending character.
3. Every command in both lowerings must pass the read-only tables: the PowerShell observation cmdlets and aliases, `ls`, `find` without action or output options, `grep`, `rg` without preprocessor or decompression options, `sed -n <N|M,N>p [file]`, and `git status`, `diff`, `log`, `show`, `branch --show-current` without repository-retargeting or external-program options.

Denial reasons name the rejected command or option so the model can rewrite the command; single-quoting is the documented way to pass an argument the classifier would otherwise refuse.

Plan mode and read-only roles accept `WriteStdin` only with empty input, which polls a command that has outlived its yield time.

## 12. Configuration

```jsonc
"Tools": {
  "Shell": {
    "Policy": {
      "Rules": [
        { "prefix": ["git", "push"], "decision": "prompt", "justification": "pushes leave the machine" },
        { "prefix": ["rm"], "decision": "forbidden" }
      ]
    }
  }
}
```

`Tools.File.RequireApprovalOutsideWorkspace` (and its thread override) selects `Prompt` or `Forbidden` for a launch directory outside the workspace. `Security.BlacklistedPaths` applies to file tools and does not restrict shell processes. The thread `ApprovalPolicy` `AutoApprove` accepts prompts, except that a dangerous command is `Forbidden`.

## 13. Security invariants

- Unknown shell selectors are rejected, never defaulted.
- Opaque scripts are evaluated as a whole; inner literals can only raise the decision.
- Plain lowering accepts a closed set of constructs; a new construct is opaque until this specification lists it.
- Wrapper inspection is depth-bounded and reports overflow as dangerous.
- Command text never determines filesystem boundaries or the running process's current directory.
- Standard input is assessed with the terminal's resolved shell and immutable launch directory, one interaction at a time per terminal.
- Approval keys carry the shell executable, so an approval for one shell never applies to another.
- Banned prefixes are never persisted as `allow` rules.
- The approval decision and the launched executable derive from one resolved shell identity.
- Lowering never starts a process.
