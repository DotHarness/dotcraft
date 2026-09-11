# DotCraft Skills Architecture Specification

| Field | Value |
|-------|-------|
| **Version** | 1.0.0 |
| **Status** | Living |
| **Date** | 2026-09-11 |
| **Related Specs** | [Session Core](session-core.md), [Prompt Composition](prompt-composition.md), [Plugin Architecture](plugin-architecture.md), [Remote Tool Host](remote-tool-host.md), [AppServer Protocol](../protocols/appserver-protocol.md) |

Purpose: define the durable architecture for DotCraft Skills, including discovery, prompt loading,
effective Skill resolution, self-learning adaptations, installation, client access, and remote use.

## 1. Architecture overview

A Skill is a named instruction bundle rooted at `SKILL.md`. It may include supporting files such as
scripts, references, assets, and interface metadata. Skills are procedural memory consumed by the
Agent through prompt context and model-callable tools; they are not executable plugins or an
alternative Agent runtime.

The Skill system has five responsibilities:

1. Discover one source Skill for each name from configured roots.
2. Describe enabled Skills and their availability in Agent context.
3. Resolve the effective source or workspace adaptation for the current runtime.
4. Load and mutate Skills through the `SkillView` and `SkillManage` tools.
5. Project Skill discovery and management through AppServer and Desktop.

Existing `SKILL.md` bundles remain loadable. Managed creation and installation apply the stricter
validation rules in this specification.

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

Names created or edited through `SkillManage`:

- contain at most 64 characters;
- start with a lowercase letter or digit;
- contain only lowercase letters, digits, hyphens, dots, and underscores;
- match the requested name in the operation.

The frontmatter requires `name` and `description`. Descriptions contain at most 1,024 characters,
and `SKILL.md` defaults to a maximum of 100,000 characters. Supporting files written through
`SkillManage` live under `scripts/` or `assets/` and default to a maximum of 1 MiB per file.

Optional metadata includes:

- `always: true`, which loads the effective instructions into active Skill context;
- requirements for executables, environment variables, and Agent tools;
- `agents/openai.yaml` interface metadata for display name, short description, default prompt,
  and icons.

Requirements affect availability. A disabled Skill remains installed but is omitted from Agent
context. An unavailable Skill may remain visible to management clients with its reason.

## 3. Sources and discovery

DotCraft resolves duplicate Skill names by source priority:

1. workspace Skills under `.craft/skills/` without the `.builtin` marker;
2. Skills contributed by enabled plugins, ordered by plugin id;
3. deployed built-in Skills under `.craft/skills/` with the `.builtin` marker;
4. user Skills under the configured user Skill root.

The first source for a case-insensitive name wins. Discovery scans direct child directories for a
`SKILL.md` file and returns descriptors sorted by name. Disabled plugin Skills and unavailable
built-ins do not enter the effective catalog.

Workspace configuration stores disabled Skill names in `Skills.DisabledSkills`. Thread and Agent
Profile policy can further restrict Skill names with `preload`, `allow`, and `deny`, and can disable
`SkillManage` with `allowManage`. Runtime policy enforces these restrictions during discovery and
invocation; prompt text is not the enforcement boundary.

## 4. Prompt loading and references

[Prompt Composition](prompt-composition.md) owns placement and caching. The Skill subsystem provides:

- active instructions for enabled, available Skills marked `always: true`;
- a compact catalog of other enabled Skills, including name, description, effective location,
  availability, and unmet requirements;
- self-learning guidance only when `SkillManage` is exposed.

The catalog instructs the Agent to call `SkillView` when a task matches a Skill. `ReadFile` is a
fallback when `SkillView` is unavailable and remains valid for an explicitly referenced supporting
file or diagnostics.

A native `skillRef` input identifies a Skill by name. Input materialization resolves the current
effective path but does not inline the instructions into the user message. Always-loaded and
profile-preloaded Skills are already present in context and do not need a `SkillView` call.

## 5. Effective Skills and adaptations

A source Skill is the selected installed bundle. A Skill variant is a workspace-local copy adapted
by self-learning for a particular source fingerprint and runtime target. The effective Skill is the
source or compatible current variant that DotCraft presents to the Agent.

Variants are stored separately from sources:

```text
.craft/
  skills/
    {skill-name}/
      SKILL.md
      ...
  skill-variants/
    {source-kind}.{skill-name}/
      {variant-id}/
        manifest.json
        skill/
          SKILL.md
          ...
```

Each variant contains a complete Skill bundle. Its manifest records the schema and variant ids,
source name, source kind, source path and fingerprint, target signature, status, timestamps,
provenance, and optional summary.

### 5.1 Source fingerprints

The source fingerprint is a SHA-256 digest over the relative path and contents of every ordinary
file in the source bundle, sorted by relative path. Dot-prefixed files, including deployment and
install markers, are excluded. A changed fingerprint makes a previously current variant stale.

### 5.2 Target signatures

The persisted target records the DotCraft harness and version, model, operating system, shell,
sandbox posture, available-tool hash, approval policy, and workspace hash. Current variant
selection compares model, operating system, shell, sandbox posture, and workspace hash. Matching is
case-insensitive except for the workspace hash.

### 5.3 Resolution

Effective resolution follows this order:

1. Resolve the source by normal source priority.
2. Compute its current source fingerprint.
3. Find a manifest with `current` status, the same fingerprint, and a compatible target.
4. Load the variant bundle when found; otherwise load the source bundle.

A current manifest with an old source fingerprint is marked `stale` and is not loaded. A restored
variant has `restored` status and is also excluded. `SkillView`, active Skill loading, Skill catalog
locations, native Skill references, and `skills/view` use the same effective resolution.

## 6. Agent tools and self-learning

`SkillView(name)` is always part of the Core Skill surface. It returns the effective `SKILL.md`
instruction body without frontmatter. It does not expose variant ids, fingerprints, manifests, or
supporting-file listings.

`SkillManage` is exposed when `Skills.SelfLearning.Enabled` is true and thread policy permits
management. Its actions are:

| Action | Behavior |
|--------|----------|
| `create` | Create a workspace source Skill from complete `SKILL.md` content. |
| `edit` | Replace the effective `SKILL.md`. |
| `patch` | Replace one or all matching strings in `SKILL.md` or a supporting file. |
| `write_file` | Add or replace a file under `scripts/` or `assets/`. |
| `remove_file` | Remove a file under `scripts/` or `assets/`. |
| `delete` | Delete an eligible workspace Skill. |

With `Skills.SelfLearning.VariantMode` set to `enabled`, edits to an existing Skill operate on a
workspace-local variant. The first edit copies the complete source bundle and then applies the
mutation. Creating a Skill still creates a workspace source. Restoring the original marks the
current compatible variant as `restored`, refreshes Skill context, and makes effective resolution
fall back to the source.

With `VariantMode` set to `disabled`, effective resolution uses source content and mutations use the
workspace source implementation. The supported values are `enabled` and `disabled`; the default is
`enabled`. Disabling self-learning removes `SkillManage` but does not remove `SkillView`.

## 7. Installation and removal

`dotcraft skill verify` validates a candidate directory without installing it. `dotcraft skill
install` verifies and publishes a candidate into `.craft/skills/{name}/`. The built-in
`skill-installer` Skill coordinates these commands for Agent-assisted installation, including the
Desktop Skill Market flow.

A candidate must contain `SKILL.md` at its root. Verification rejects escaped or invalid paths,
hidden control paths other than the DotCraft install marker, more than 1,000 files, supporting files
over 1 MiB, and bundles over 100 MiB. Installation stages the validated bundle, writes provenance
and source fingerprint metadata, and atomically replaces an existing workspace source only when
overwrite is explicit. Built-in Skills cannot be overwritten.

`--name` supplies the canonical local install name. It accepts ASCII letters, digits, hyphens,
dots, and underscores. When present, the candidate frontmatter name does not need to match it.
Installation preserves ordinary relative supporting files instead of rewriting the candidate.

`skills/uninstall` removes the resolved workspace or user Skill and its stored variants. Plugin and
built-in Skill lifecycle remains owned by their plugin or built-in deployment mechanism.

## 8. AppServer and Desktop

[AppServer Protocol](../protocols/appserver-protocol.md) owns exact wire shapes. The Skill methods
have these responsibilities:

| Method | Responsibility |
|--------|----------------|
| `skills/list` | List descriptors, availability, source metadata, enabled state, and `hasVariant`. |
| `skills/read` | Read the source `SKILL.md` and metadata. |
| `skills/view` | Read the effective source-or-variant instruction body. |
| `skills/restoreOriginal` | Restore source behavior for the current runtime target. |
| `skills/setEnabled` | Persist workspace enablement. |
| `skills/uninstall` | Remove an eligible installed Skill and its variants. |

`capabilities.skillsManagement` announces the management methods. `capabilities.skillVariants`
means variant mode is enabled for the current runtime. `skills/view` remains available as a
source-only effective view when variant mode is disabled. Desktop uses `hasVariant` and
`skillVariants` to show the adaptation badge and Restore Original action without exposing manifest
details.

## 9. Remote execution

Connecting a [Remote Tool Host](remote-tool-host.md#12-execution-locations-and-file-transfer) does
not move or synchronize Skill bundles, variants, or plugin packages. `SkillView`, supporting files,
and effective resolution remain Agent-local.

RPC-eligible file tools accept `target: "local"`, so the Agent can inspect a Skill's supporting
files while its default route is remote. When remote execution needs a script, directory, or CLI
file, the Agent copies the required content with `RemoteToolHost.Transfer` and preserves any relative
dependencies. Copying a plugin package does not activate the plugin or install its dependencies on
the remote Host.

## 10. Security and invariants

- Source Skills are not mutated by self-learning while variant mode is enabled.
- Variant storage is not scanned as a source root.
- Stale and restored variants are not selected as effective Skills.
- General file tools remain subject to their normal path and approval policies.
- Skill creation, deletion, installation, and overwrite retain their approval and validation boundaries.
- Supporting files become executable only when invoked through an ordinary tool with its own policy.
- Plugin-contained Skill identity and lifecycle remain subordinate to the owning plugin.
- AppServer clients do not need variant internals for ordinary Skill use.
