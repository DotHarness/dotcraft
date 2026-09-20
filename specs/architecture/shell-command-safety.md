# Shell Command Safety

| Field | Value |
|---|---|
| Version | 1.1.0 |
| Status | Living |
| Date | 2026-09-19 |
| Owner | DotCraft.Core (`DotCraft.Security.ShellCommands`, `DotCraft.Tools.ShellTools`) |
| Related Specs | [Tool Architecture](tools-architecture.md), [Session Core](session-core.md), [Remote Tool Host](remote-tool-host.md), [SubAgents](../features/subagents.md), [AppServer Protocol](../protocols/appserver-protocol.md) |

## 1. Purpose

This specification defines how DotCraft decides whether a shell command issued through the `Exec` tool runs, prompts for approval, or is rejected, how that decision is bound to the executable that will run the command, and what an approval remembers.

It replaces string and regular-expression heuristics with one kernel: resolve the shell identity, lower the script into argument vectors, match policy rules, fall back to workspace and danger checks, aggregate, then approve against a structured key. The same kernel classifies read-only commands for Plan mode and read-only SubAgent roles.

### 1.1 Ownership

- This document owns shell identity, lowering, dangerous-command detection, policy rules, decision aggregation, the approval key, approval memory, and the execution gate.
- [Tool Architecture](tools-architecture.md) owns the dispatch pipeline; the shell gate is the runtime-stage authority for `Exec`.
- [Session Core](session-core.md) owns approval items and turn integration; the request fields in Section 9 are projected there and in the [AppServer Protocol](../protocols/appserver-protocol.md).
- The [SubAgent](../features/subagents.md) specification owns roles; Section 8 defines what its `readOnly` shell access admits.

## 2. Non-goals

- Process isolation. The kernel reasons about command text; it does not confine what an approved process can touch. Sandbox mode keeps the container boundary as its authority and does not use this gate.
- Proving that an interpreter argument is harmless. `python -c`, `node -e`, and similar arguments are code; the kernel treats them as opaque words.
- Parsing `cmd.exe` scripts. Cmd scripts are opaque and only scanned for dangerous literals.
- Proving where a script leaves the shell. Directory tracking follows the changes it can read and assumes each one ran and succeeded. It cannot see a `cd` that failed, a branch the shell skipped, or a subshell, so a deliberately built chain can make the kernel check a later command against a directory the shell is not in. Tracking narrows accidents; it is not a boundary against crafted input.

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

The `Exec` tool takes the script text and an optional `shell` selector; the host chooses and launches the shell. `ShellIdentityResolver` turns the selector into a `ShellIdentity { Kind, ExecutablePath }` and the gate launches exactly `ExecutablePath`.

| Platform | Selector | Resolution |
|---|---|---|
| Windows | empty, `powershell`, `powershell.exe` | `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe` |
| Windows | `pwsh`, `pwsh.exe` | `PATH` lookup; must resolve |
| Windows | `cmd`, `cmd.exe` | `%SystemRoot%\System32\cmd.exe` |
| Unix | empty | `/bin/bash` |
| Unix | `bash`, `sh`, `zsh`, `pwsh`, or a path whose file name is one of them | `PATH` lookup or the given absolute path; must exist |

Any other selector is `Forbidden` with a reason naming the selector. The resolver never treats an unknown selector as the default shell and never launches a bare name.

`ShellKind` is one of `PowerShell`, `Pwsh`, `Cmd`, `Bash`, `Sh`, `Zsh`. `ShellFamily` groups them as `PowerShell`, `Cmd`, and `Posix`; the family selects the lowerer.

Lifecycle hooks resolve their shell through the same resolver so hook commands and tool commands cannot disagree about what `pwsh` means.

## 5. Lowering

`IShellScriptLowerer.Lower(script)` returns a `LoweredScript`:

```
LoweredScript
├── Family
├── PlainCommands: string[][] | null   // non-null only when plain lowering succeeded
├── PlainRejectReason: string | null   // first reason plain lowering failed
└── LiteralCommands: string[][]        // literal extraction; equals PlainCommands when plain
```

Plain lowering must fail closed: any construct not listed as accepted makes the script opaque. Lowerers never guess the runtime meaning of a construct they do not model.

### 5.1 PowerShell family

The lowerer parses with the PowerShell language parser (`System.Management.Automation.Language.Parser`) in process. No PowerShell process starts during lowering.

Plain lowering accepts a script only when all of the following hold:

- The parser reports no errors.
- Every AST node is one of `ScriptBlockAst`, `NamedBlockAst`, `PipelineChainAst`, `PipelineAst`, `CommandAst`, `StringConstantExpressionAst`, `CommandParameterAst`.
- `ScriptBlockAst.ScriptRequirements`, `UsingStatements`, `ParamBlock`, `DynamicParamBlock`, `BeginBlock`, `ProcessBlock`, `CleanBlock`, and `EndBlock.Traps` are all empty. These regions execute code that the statement list does not show.
- Every `CommandAst` uses the plain invocation form (no `&` or `.` operator) and has no redirections.
- No word is the stop-parsing token `--%`.

Words are the parser's constant values: single-quoted, double-quoted without expansion, here-strings without expansion, and bare strings are decoded by the parser, so escapes and Unicode quote or dash aliases carry their real runtime value. A `CommandParameterAst` lowers to `-Name`; a parameter with an attached argument (`-Name:value`) lowers to `-Name:` followed by the argument value. A numeric literal is accepted only when its runtime value is spelled exactly as written (`100`, not `01`, `0x10`, `1kb`, or `1e3`); any other numeric literal makes the script opaque.

Literal extraction walks every `CommandAst` in the tree, including those nested in script blocks and expressions, and collects its constant words in order.

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

`DangerousCommandDetector` inspects lowered commands and returns a match kind of `ForcedRemove` or `Other`.

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

A `ShellPrefixRule` is `{ prefix: string[], decision: allow | prompt | forbidden, justification?: string }`. A rule matches a lowered command when the command has at least as many words as the prefix and each prefix word equals the corresponding command word (ordinal comparison; case-insensitive on Windows for the first word).

Matching looks up rules by the command's first word. When no rule matches and the first word is a path, matching retries with the file name (Windows extensions stripped). All matching rules apply; the most severe decision wins, so a broad `prompt` rule is not overridden by a narrower `allow` rule.

Rules come from the effective layered configuration value of `Tools.Shell.Policy.Rules` followed by the learned rules in `.craft/security/shell-rules.json`. Equal rules are kept once.

The effective rule set has a fingerprint (hash of the sorted rule texts) that participates in the approval key.

### 7.2 Banned prefixes

Some prefixes are too broad to remember as `allow` rules: shells and interpreters with inline-code flags (`bash -c`, `sh`, `zsh`, `cmd /c`, `powershell -Command`, `pwsh -c`, `python -c`, `node -e`, `perl -e`, `ruby -e`, `deno eval`, `bun -e`), wrappers (`env`, `sudo`, `xargs`, `nohup`, `timeout`), destructive verbs (`rm`, `del`, `rmdir`, `Remove-Item`), and version-control roots (`git`, `npm run`). A prefix that exactly equals a banned entry is never persisted; the kernel falls back to an exact approval key. Longer prefixes that merely start with a banned entry are allowed.

## 8. Evaluation

`ShellCommandSafetyKernel.Evaluate(request)` produces a `ShellAssessment { Decision, Reasons, Risk, Shell, Lowering, Matches, ApprovalKey, Remember }`.

1. Resolve the shell identity. Failure is `Forbidden`.
2. Lower the script with the family lowerer.
3. Build the command list: `PlainCommands` when plain; otherwise a single synthetic command `[<family sentinel>, <script>]` where the sentinel is `__shell_script__`, `__powershell_script__`, or `__cmd_script__`. An opaque script is never reduced to its first inner executable.
4. For each command, match rules. A rule match yields its decision and skips the fallback for that command.
5. For each unmatched command, apply the fallback:
   - a dangerous match yields `Prompt`, or `Forbidden` when the thread auto-approves prompts;
   - otherwise apply the workspace check: the working directory must lie inside a workspace root, no path evidence may resolve outside every root, and no path evidence may hit the path blacklist. A blacklist hit is `Forbidden`. Outside evidence is `Prompt` when the thread requires approval outside the workspace and `Forbidden` otherwise. A clean check is `Allow`.
6. The assessment decision is the maximum over all commands.

Path evidence comes from `PathEvidenceScanner`: for plain commands it inspects each word; for opaque scripts it scans the script text. It recognizes absolute paths, `~`, `$HOME`, drive letters, `%VAR%`, `$env:VAR`, and UNC paths, resolves them, and only ever raises the decision. A word that climbs with `..` is resolved against the working directory; in an opaque script such a climb counts as the parent of the working directory. The device names `/dev/null`, `/dev/stdin`, `/dev/stdout`, `/dev/stderr`, `/dev/zero`, `/dev/random`, `/dev/urandom`, `/dev/tty`, `NUL`, `CON`, `PRN`, and `AUX` are not evidence.

Directory changes move the working directory for the commands that follow. In a plain script the changes are `cd`, `pushd`, and `popd` (Posix) and `cd`, `chdir`, `sl`, `Set-Location`, `pushd`, `Push-Location`, `popd`, and `Pop-Location` (PowerShell). A literal target is resolved against the current directory, counts as path evidence for that command, and becomes the directory every later command is checked in. A change without a resolvable target (no argument, `-`, or a stack pop) leaves the directory unknown, so that command and every later one are treated as outside the workspace.

An opaque script is treated the same way when literal extraction puts a directory-changing word in a command position. This is a heuristic over extracted literals and not a proof in either direction: a change the extraction cannot see, such as an invoked variable or `eval`, is missed, and a directory-changing word that appears only as an argument or inside a string does not count. A caller that already knows the directory is undeterminable says so on the request, and every command in that script is then evaluated as running in an undeterminable directory.

The assessment reports the directory it tracked through the last command, or nothing when it ended undeterminable. That report is the same best-effort tracking the commands were checked against, with the limit Section 2 states: it assumes every change it read ran and succeeded.

Workspace containment is decided by `WorkspaceBoundary`, the single implementation shared with file tools; it resolves symbolic links before comparing.

An `Allow` produced by an explicit `allow` rule bypasses danger detection for that command. Danger detection is part of the fallback, not a veto above rules.

## 9. Approval

### 9.1 Request

When the decision is `Prompt`, the gate raises a `ShellApprovalRequest`:

| Field | Content |
|---|---|
| `command`, `workingDirectory` | the raw script and resolved directory |
| `shell` | `{ kind, executable }` |
| `commands` | plain lowering result, or empty when opaque |
| `lowering` | `plain` or `opaque`, with the reject reason when opaque |
| `risk` | `{ level, reasons[] }` where level is `dangerous`, `outsideWorkspace`, or `rule` |
| `approvalKey` | Section 9.2 |
| `remember` | what each remembering decision will store (Section 9.3) |

The approval service receives the whole request together with the ambient `ApprovalContext`. Decorators that route or label approvals carry their labels in the context and never rewrite the command text.

### 9.2 Approval key

```
ShellApprovalKey
├── shellKind, shellExecutablePath
├── workingDirectory                  // full path; case-folded on Windows
├── canonicalCommand: string[]        // plain single command: its words
│                                     // plain multi-command: words joined with "&&" markers
│                                     // opaque: [sentinel, script]
└── policyFingerprint
```

The key hash is SHA-256 over the canonical JSON of these fields. Two invocations share a key only when the same shell executable would run an identical canonical command in the same directory under the same rule set.

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

The `remember` field of the request describes these outcomes. The approval request sent to clients carries `reasons`, `rememberedPrefixes`, and `remembersExactCommand` so a client can state the scope of a remembering decision next to that option; the command text and directory travel as `operation` and `target`.

## 10. Execution gate

`ShellExecutionGate` is called by `ShellTools.Exec` after the invocation is recorded and before the process starts. It is the single enforcement point: every host-side `Exec`, including SubAgent and remote-host instances, constructs `ShellTools` and therefore passes the gate.

```
AuthorizeAsync(command, shellSelector, workingDirectory)
  assessment = kernel.Evaluate(...)
  Forbidden        -> tool error carrying the reasons
  Prompt           -> approval service; declined -> tool error
  Allow / approved -> ShellIdentity handed to the terminal service
```

The terminal service accepts only a `ShellIdentity` and launches `ExecutablePath`. The gate and the launch use the same resolved identity, so the executable that was assessed is the executable that runs.

The dispatcher's approval stage declares no approval for `Exec`; the gate owns it. The dispatcher's policy stage still enforces read-only classification (Section 11) and role or mode restrictions before the runtime stage.

### 10.1 Standard input

An admitted command can be an interpreter that keeps reading its standard input, so `ShellTools.WriteStdin` is a second way into a process the gate already started. Because this specification does not confine what an approved process may touch, that input is the only remaining place to decide, and the gate assesses it with the same kernel before it is written.

A running terminal carries a stdin session: the `ShellIdentity` it was launched with, the working directory the kernel last knew it to be in, and whether that directory is still known. The assessment uses that identity rather than resolving a selector, because the process is already running and no selector can change it. After an admitted write the session takes the directory the assessment tracked; when the assessment ended undeterminable, the session's directory becomes unknown and every later write to it is evaluated as running in an undeterminable directory. A session never regains a known directory.

The session starts from the command that launched the terminal, not from the directory that command was launched in: a terminal opened with `cd elsewhere && bash` is running elsewhere, and seeding it with the launch directory would admit later relative input the person never approved. The launch assessment's reported directory therefore seeds the session, and a launch whose directory the assessment could not report starts unknown. A terminal a client starts has no assessment and starts from its launch directory.

One terminal admits one interaction at a time. Assessment, approval and the write are serialized per terminal, and the session state is read after that turn is acquired, so a write cannot be authorized against a directory that another write has already moved the terminal out of. Terminals do not block each other.

Empty input polls for output and is not assessed. Input that is exactly the end-of-text character is an interrupt rather than a command and is not assessed. Terminal input a client sends on the person's behalf does not pass the gate; the person typing it is the authority.

An approval raised for standard input carries the input as its command and the session's directory as its working directory, and states that the text is being written to a running terminal whose own directory and state may since have moved.

## 11. Read-only classification

`ReadOnlyCommandClassifier.IsReadOnly(command, shellSelector)` decides whether a command is a non-mutating observation. It is used by Plan mode and by SubAgent roles with `readOnly` shell access. Read-only is a property of the command text under every supported shell, so the classifier does not depend on the host shell:

1. Any shell selector is denied; the selector changes which executable runs.
2. The script must lower plainly under both the Posix and the PowerShell lowerer. The Posix reject reason is reported first because it names the offending character.
3. Every command in both lowerings must pass the read-only tables: the PowerShell observation cmdlets and aliases, `ls`, `find` without action or output options, `grep`, `rg` without preprocessor or decompression options, `sed -n <N|M,N>p [file]`, and `git status`, `diff`, `log`, `show`, `branch --show-current` without repository-retargeting or external-program options.

Denial reasons name the rejected command or option so the model can rewrite the command; single-quoting is the documented way to pass an argument the classifier would otherwise refuse.

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

`Tools.File.RequireApprovalOutsideWorkspace` (and its thread override) selects `Prompt` or `Forbidden` for outside-workspace evidence. `Security.BlacklistedPaths` feeds the blacklist check. The thread `ApprovalPolicy` keeps its meaning: `AutoApprove` accepts prompts except that a dangerous command is `Forbidden`.

## 13. Security invariants

- Unknown shell selectors are rejected, never defaulted.
- Opaque scripts are evaluated as a whole; inner literals can only raise the decision.
- Plain lowering accepts a closed set of constructs; a new construct is opaque until this specification lists it.
- Wrapper inspection is depth-bounded and reports overflow as dangerous.
- A directory change the kernel cannot follow makes the rest of the script outside the workspace.
- Input written to a running terminal is assessed against the shell that terminal is running, and a directory change the kernel cannot follow there makes every later write to that terminal outside the workspace.
- A terminal's session starts where its launch assessment said the shell ends up, and unknown when that assessment reported nothing.
- One terminal authorizes and writes one interaction at a time.
- Approval keys carry the shell executable, so an approval for one shell never applies to another.
- Banned prefixes are never persisted as `allow` rules.
- The approval decision and the launched executable derive from one `ShellIdentity` instance.
- Lowering never starts a process.

## 14. Conformance

Implementations must ship fixture-driven tests for: PowerShell lowering (accepted forms and every rejected construct in Section 5.1), Posix lowering (Section 5.2 accepted and rejected forms), dangerous-command detection per family, rule matching including severity aggregation and file-name fallback, directory-change tracking across chained commands and across successive writes to one terminal, a terminal seeded from a launch command that changed directory, the directory-changing word appearing only as an argument in an opaque script, approval-key equality and inequality across shell, directory, and rule-set changes, read-only classification, and shell identity resolution on both platforms. A non-Windows smoke test must parse a PowerShell script through the lowerer to prove the parser loads without a PowerShell installation.
