# DotCraft Skills Architecture Specification

| Field | Value |
|-------|-------|
| **Version** | 0.10.0 |
| **Status** | Living |
| **Date** | 2026-10-26 |

Purpose: define the durable architecture for DotCraft Skills, including discovery, prompt loading,
authoring, installation, client access, and remote use.

## 1. Architecture overview

A Skill is a named instruction bundle rooted at `SKILL.md`. It may include supporting files such as
scripts, references, assets, and interface metadata. Skills are procedural memory consumed by the
Agent through prompt context and model-callable tools; they are not executable plugins or an
alternative Agent runtime.

The Skill system has four responsibilities:

1. Discover one Skill for each name from configured roots.
2. Describe enabled Skills in Agent context.
3. Load Skill instructions through the `SkillView` tool.
4. Project Skill discovery and management through AppServer and Desktop.

A Skill is an ordinary directory that the Agent changes with general file and shell tools (§5).

Any `SKILL.md` bundle is loadable; verification and installation apply the stricter validation
rules below.

## 2. Skill bundles and metadata

A Skill directory contains `SKILL.md` at its root. Managed Skills use YAML frontmatter followed by
a non-empty instruction body:

```markdown
---
name: code-review
description: Review code changes for correctness and maintainability.
---

Follow the repository review workflow.
```

Names accepted by verification and installation:

- contain at most 64 characters;
- start with an ASCII letter or digit;
- contain only ASCII letters, digits, hyphens, dots, and underscores.

The frontmatter requires `name` and `description`. Descriptions contain at most 1,024 characters,
and `SKILL.md` contains at most 100,000 characters. Supporting files are limited as described in §7.

Optional metadata includes:

- `always: true`, which loads the instructions into active Skill context;
- requirements for executables, environment variables, and Agent tools;
- `agents/openai.yaml` interface metadata for display name, short description, default prompt,
  and icons.

Requirements are declarative metadata for consumers and do not affect discovery or Agent context.
A disabled Skill remains installed but is omitted from Agent context.

## 3. Sources and discovery

DotCraft resolves duplicate Skill names by source priority:

1. workspace Skills under `.craft/skills/` without the `.builtin` marker;
2. Skills contributed by enabled plugins, ordered by plugin id;
3. deployed built-in Skills under `.craft/skills/` with the `.builtin` marker;
4. user Skills under the configured user Skill root;
5. shared Skills under `~/.agents/skills`.

The first source for a case-insensitive name wins. Discovery scans direct child directories for a
`SKILL.md` file and returns descriptors sorted by name. Skills owned by disabled plugins and
suppressed built-ins do not enter the effective catalog.

`~/.agents/skills` is a cross-tool convention that other Agent products also read. DotCraft
discovers it as a second user-level root, so those Skills report the `user` source and differ only
by path. `Skills.IncludeSharedSkills` turns the root off and defaults to true; it takes effect on
process start. DotCraft does not own that directory, so a Skill resolved from it can be disabled
but not uninstalled.

Workspace configuration stores disabled Skill names in `Skills.DisabledSkills`. Thread and Agent
Profile policy can further restrict Skill names with `allow` and `deny`. When `allow` is absent,
every enabled Skill is available; deny wins over allow; names match case-insensitively. Runtime policy enforces these restrictions: an
excluded Skill is left out of active Skill context and the catalog, and `SkillView` refuses it.
Prompt text is not the enforcement boundary.

## 4. Prompt loading and references

[Prompt Composition](prompt-composition.md) owns placement and caching, and
[World State](world-state.md) owns how a change reaches the model. The Skill subsystem provides:

- active instructions for enabled, permitted Skills marked `always: true`, in base instructions;
- routing guidance in base instructions, telling the Agent to call `SkillView` when a task matches
  a Skill. `ReadFile` is a fallback when `SkillView` is unavailable and remains valid for an
  explicitly referenced supporting file or diagnostics;
- the catalog of enabled, permitted Skills, with name, description, location, and whether each is
  always loaded, as the `skills` world-state section.

Base instructions do not direct the Agent to create or revise Skills. Authoring guidance lives in the
built-in `skill-authoring` Skill, which the Agent selects like any other Skill (§5).

### 4.1 Catalog freshness

A thread reads the catalog before its first sampling request and again only after the Skill loader
reports a change: an install, uninstall, or enablement change made through this host's Skill
service, a plugin change, or the `/reload-skills` command ([Session Core](session-core.md) §13.3).
Other changes on disk are not reported, including Agent edits and a `dotcraft skill install` run in
another process; they reach the catalog after the next reported change. `SkillView` always reads
the current files. When the catalog differs from what the model was last shown, the complete current
catalog is appended to history and replaces the earlier one. Base instructions and earlier history
are never rewritten, so the cached prompt prefix survives a Skill change, and a thread sees a
reported change on its next sampling request.

Always-loaded instructions stay in base instructions and are pinned until compaction.

### 4.2 References

A native `skillRef` input identifies a Skill by name. Input materialization resolves the Skill's
current path but does not inline the instructions into the user message. Always-loaded Skills are
already present in context and do not need a `SkillView` call.

## 5. Authoring and changing Skills

The Agent creates and changes Skills with the general file and shell tools, under those tools' path
and approval policies. DotCraft does not validate, approve, or record these edits separately.

The built-in `skill-authoring` Skill describes bundle structure, where a new Skill belongs, how to
reference supporting files, and how to check the result with `dotcraft skill verify` (§7). It is
listed and loaded like any other Skill. A Skill the Agent creates or renames enters the catalog after
the next reported change (§4.1), while `SkillView` can load it at once.

Where a Skill lives decides who owns an edit:

- A workspace Skill belongs to the workspace, and edits persist.
- Built-in deployment rewrites a directory carrying the `.builtin` marker whenever the product
  version changes, so an edit there lasts only until the next deployment. Removing the marker turns
  the directory into a workspace Skill that deployment leaves alone and no longer updates.
- A plugin-contained Skill is replaced by its plugin's lifecycle.
- A user or shared Skill is visible to every workspace, and the shared root to other Agent
  products.

To adapt a plugin, user, or shared Skill for one workspace only, the Agent copies it into
`.craft/skills/` under the same name, where it takes precedence by source priority (§3).

### 5.1 Presentation

A file tool call whose target lies inside a Skill directory is a Skill operation. A Skill directory
is a direct child directory of a configured Skill root (§3): the workspace root, an enabled plugin's
Skill directory, the user root, or the shared root. When recording the Item, Core marks the call in
its trusted presentation options with `skill`, `skillName` (the directory's name), and `skillFile`
(the absolute path of the directory's `SKILL.md`, which may not exist yet):

- `skill: "read"` for a read or search;
- `skill: "create"` for a write to the directory's `SKILL.md` when none exists as the call starts;
- `skill: "update"` for any other write or edit.

A failed or rejected call keeps its mark together with its failure. Only Core resolves Skill roots,
so clients rely on this mark rather than on paths. Shell commands are not classified.

Clients show a read as reading the Skill, and a create or update as creating or updating the Skill,
whose changes can be reviewed like a file edit. The Skill name opens that Skill.

## 6. Agent tool

`SkillView(name)` is always part of the Core Skill surface. It returns the instruction body of the
resolved `SKILL.md` without frontmatter. It does not expose supporting-file listings. A disabled or
policy-excluded Skill is refused.

## 7. Installation and removal

`dotcraft skill verify` validates a candidate directory without installing it. `dotcraft skill
install` verifies and publishes a candidate into `.craft/skills/{name}/`. The built-in
`skill-installer` Skill coordinates these commands for Agent-assisted installation, including the
Desktop Skill Market flow.

A candidate must contain `SKILL.md` at its root. Verification rejects escaped or invalid paths,
hidden control paths other than the DotCraft install marker, more than 1,000 files, supporting files
over 1 MiB, and bundles over 100 MiB. Installation stages the validated bundle, writes provenance
and source fingerprint metadata, and atomically replaces an existing workspace source only when
overwrite is explicit. Built-in Skills cannot be overwritten. The source fingerprint is a SHA-256
digest over the relative path and contents of every ordinary file in the bundle, sorted by relative
path; dot-prefixed files, including deployment and install markers, are excluded.

`--name` supplies the canonical local install name. It accepts ASCII letters, digits, hyphens,
dots, and underscores. When present, the candidate frontmatter name does not need to match it.
Installation preserves ordinary relative supporting files instead of rewriting the candidate.

`skills/uninstall` removes the resolved workspace or user Skill. Plugin and built-in Skill
lifecycle remains owned by their plugin or built-in deployment mechanism.

## 8. Management boundary

Hosts expose discovery, reads, enablement, and eligible removal through the same Skill service. A
descriptor reports source identity and enabled state.

## 9. Execution locality

Skill discovery and loading remain local to the Agent. Selecting a remote execution location does
not synchronize or activate bundles or plugins. Supporting files needed elsewhere must be explicitly
transferred while preserving relative dependencies.

## 10. Security and invariants

- DotCraft itself changes Skill files only through installation, uninstallation, and built-in
  deployment.
- Agent edits to Skill files remain subject to the general file and shell tool path and approval
  policies.
- Installation and overwrite retain their validation boundaries.
- Supporting files become executable only when invoked through an ordinary tool with its own policy.
- Plugin-contained Skill identity and lifecycle remain subordinate to the owning plugin.
