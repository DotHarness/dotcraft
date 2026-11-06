# DotCraft Plugin Authoring Specification

| Field | Value |
|-------|-------|
| **Version** | 0.10.3 |
| **Status** | Draft |
| **Date** | 2026-11-07 |
| **Parent** | [Plugin Architecture](../architecture/plugin-architecture.md) |
| **Related** | [Skills](../architecture/skills.md), [Plugin Registry](../architecture/plugin-registry.md), [.NET Plugins](../architecture/dotnet-plugins.md), [Desktop Plugins](../architecture/desktop-plugins.md), [Exec CLI](exec-cli.md) |

Purpose: define how a user turns a workflow into a complete DotCraft plugin from any workspace, how that plugin is verified and changed, and how it reaches other people.

## 1. Overview

Plugin authoring runs inside ordinary user workspaces. It cannot assume a DotCraft source checkout, a Python installation, or access to repository documentation. The built-in `plugin-creator` Skill is the authoring entry point, and it covers one lifecycle:

1. **Create**: settle the purpose, choose contributions, and write a complete plugin.
2. **Verify**: pass `dotcraft plugin verify` and exercise the plugin for real.
3. **Update**: change a plugin at the source that owns it while keeping its identity.
4. **Share**: keep it private, publish it through a marketplace the author controls, or contribute it to the official registry.

A plugin is complete only when it would look finished in the plugin catalog and has been shown to work. A manifest that the runtime accepts is not enough, because the runtime deliberately tolerates missing presentation metadata ([Plugin Architecture](../architecture/plugin-architecture.md) §2.4). Authoring is stricter than loading, and loading rules do not change.

## 2. Goals and non-goals

Goals:

- Any user can create a plugin from a non-source workspace without knowing the manifest format.
- Required steps such as the icon are enforced by a tool result rather than remembered by the model.
- An author who wants to contribute has one defined route from a local plugin to an official listing.

Non-goals:

- Hosted plugins, account-scoped plugin storage, or a submission portal.
- New manifest fields or contribution types.
- A product feature for plugin evaluation, benchmark viewers, or automated Skill description tuning.
- Changing what discovery, installation, or trust accept.

## 3. Skill boundaries

| Skill or source | Owns |
|---|---|
| `plugin-creator` | The plugin lifecycle in §1, contribution selection, presentation rules, and the contribution route |
| `skill-creator` | Creating and changing a Skill, whether standalone or inside a plugin |
| `create-hooks` | Hook events, scripts, and their configuration |
| `workflow-authoring` | Dynamic Workflow scripts |
| Public documentation | Contribution APIs, the manifest field reference, and SDK contracts |

`plugin-creator` remains one Skill with detail loaded by lifecycle stage and contribution type. It sends the Agent to the owning Skill for the content of a contribution, and it does not restate that Skill's rules or the public API reference. A plugin Skill also carries interface metadata ([Skills](../architecture/skills.md) §2) so that it does not appear without a name or icon, and it may reuse the plugin's own assets.

Selection boundary:

- A request for reusable instructions in one workspace or for one person is a standalone Skill. It selects `skill-creator`.
- A request to bundle several contributions, to run executable behavior, or to hand a capability to other people is a plugin. It selects `plugin-creator`.
- A failed Skill installation is not a reason to create a plugin.
- Installing, enabling, or configuring an existing plugin is not authoring.

## 4. Creating a plugin

### 4.1 Settle the purpose

The Agent starts from what the conversation already shows, including a workflow the user just performed. It asks only about gaps that would change the result, and it never asks the user to author manifest fields.

Before writing files, the Agent states one sentence of purpose and the plan: plugin id, location, and contributions. It waits for the user when the purpose or the contributions are ambiguous. Otherwise it continues.

An id that already exists among discovered plugins moves the work to §8 unless the user asks for a separate plugin under a new id. A new id is lowercase hyphen-case with at most 64 characters, and the plugin directory name equals it.

### 4.2 Choose contributions

| The user needs | Contribution | Content owner |
|---|---|---|
| Repeatable instructions or a workflow the Agent follows | Skills | `skill-creator` |
| A prompt the user invokes by name | Commands | Public documentation |
| Tools or data from an external service | MCP servers | Public documentation |
| Automation on Agent lifecycle events | Hooks | `create-hooks` |
| A scripted multi-agent procedure | Dynamic Workflows | `workflow-authoring` |
| Language intelligence for a file type | LSP servers | Public documentation |
| An external application connected to threads | App descriptors | Public documentation |
| A view, action, or tool presentation in Desktop | Desktop extension | Public documentation |
| In-process tools or Host contribution points | .NET extension | Public documentation and `DotNetPlugin.Inspect` |
| Options the user changes per installation | Settings | Public documentation |

The Agent chooses the contributions with the least execution authority that meet the need. A Desktop or .NET extension runs as trusted code, so it needs a reason that a Skill or MCP server cannot satisfy. One plugin may combine contributions under one id.

### 4.3 Choose the location

| Audience | Location |
|---|---|
| This workspace, by default | `<workspace>/.craft/plugins/<id>` |
| Every workspace of this user | `<craft-home>/plugins/<id>` |
| A .NET extension under development | `<workspace>/.craft/plugin-projects/<id>` ([.NET Plugins](../architecture/dotnet-plugins.md) §7.6) |

The Agent does not write into marketplace snapshots or into plugin directories that carry the managed marker.

### 4.4 Write from templates

`plugin-creator` ships a template for the manifest and for each contribution with a fixed file shape. Workflows and app descriptors take their shape from their owners in §4.2. The Agent copies the relevant templates with ordinary file tools and writes real content. Authoring needs no scaffold script and no interpreter beyond what a chosen contribution itself requires, such as Node.js to build a Desktop extension.

Rules:

- The Agent adds only the directories and files the plugin uses.
- Values the Agent must supply appear in templates as `[TODO: ...]`. None may remain at completion.
- Templates contain no icon, so a missing icon cannot pass as a generic one.
- Host-pinned values, such as the Desktop SDK package version and `dotnet.minHostVersion`, come from the running DotCraft.
- Hook commands and MCP launch configuration reference plugin files through plugin-relative paths or plugin root variables, never absolute paths.
- Secrets stay out of plugin files. Configuration references environment variables, and setup instructions name those variables.

## 5. Presentation metadata

### 5.1 Required fields

Every authored plugin, including a .NET-only plugin, declares these fields:

| Field | Rule |
|---|---|
| `displayName`, `interface.displayName` | A recognizable name of at most 30 characters |
| `description` | One sentence of at most 160 characters |
| `interface.shortDescription` | One line of at most 48 characters |
| `interface.developerName` | The real author or team. DotCraft or DotHarness appears only on plugins they author |
| `interface.category` | Exactly one of `Coding`, `Design`, `Engineering`, `Security`, `Lifestyle`, `Productivity`, or `Research` |
| `capabilities`, `interface.capabilities` | The contributions the plugin actually ships |
| `interface.defaultPrompt` | One line of at most 128 characters |
| `interface.logo`, `interface.composerIcon` | See §5.3. Both may name the same file |

`interface.longDescription` is optional, has at most 300 characters, and exists only when it adds behavior or audience information that the short description cannot hold.

### 5.2 Copy rules

Copy states what the plugin does, and nothing else.

- Write one plain statement of the capability.
- Leave out hypotheticals, advice, setup instructions, implementation detail, and marketing language.
- Do not open with "This plugin" or restate the display name.
- Make `defaultPrompt` a realistic first request that shows the plugin at work.
- Use the user's language for a private plugin and English for the official registry.

| Field | Avoid | Prefer |
|---|---|---|
| `shortDescription` | `A plugin that may help if you need to review changes` | `Review pull requests against team rules` |
| `description` | `This plugin provides tools. Configure a token first.` | `Reviews pull requests against your team's written rules.` |
| `defaultPrompt` | `Use Review Tools.` | `Review this branch against our review rules.` |

### 5.3 Icons

A plugin owns original or properly licensed artwork that relates to what it does. The Agent reuses branding the user supplies. Otherwise it draws a simple icon, and a user's brand mark or a third party's mark is used only with permission.

- Formats are SVG, PNG, JPEG, and WebP, at most 512 KiB per file, inside the plugin root. SVG is preferred.
- Icons are square. Raster files are at least 256 × 256 for `logo` and 48 × 48 for `composerIcon`.
- The silhouette stays legible at small size on light and dark backgrounds. An icon that needs a background includes it ([Plugin Architecture](../architecture/plugin-architecture.md) §2.4).
- SVG is self-contained geometry with no scripts, event handlers, external references, embedded HTML, or font text.

When the Agent cannot view the rendered icon, it reports that the icon was checked by file only.

## 6. `dotcraft plugin verify`

```text
dotcraft plugin verify --candidate <plugin-root> [--for-publish] [--json]
```

The command checks one plugin root without installing it. It uses the runtime's own manifest and descriptor parsing, so verification cannot accept a shape the runtime rejects. It writes nothing, uses no network, and never loads or runs plugin code. For a .NET project, the candidate is the project's `plugin/` bundle after `DotNetPlugin.Build`. When `dotcraft` is not on PATH, as on a Desktop-only installation, the Agent locates the installed binary. If it still cannot run, the plugin is reported as unverified, not complete.

| Group | Applies | Checks |
|---|---|---|
| Runtime | Always | Manifest errors, path rules, and declared files. MCP, LSP, hook, app, and settings descriptors. Declared Desktop output. .NET metadata preflight. Every plugin Skill under the rules of `dotcraft skill verify` |
| Completeness | Always | Remaining `[TODO: ...]` in the manifest, descriptors, and plugin Skills. Required fields and limits in §5.1. Icon rules in §5.3 that a file can prove |
| Publication | `--for-publish` | Strict SemVer `version` and hyphen-case id. No `.git`, `node_modules`, `.builtin`, build intermediates, or symbolic links. No literal credential values in descriptors and no recognizable token in text files. No absolute machine paths in descriptors |

Findings in these groups are errors. Warnings cover findings that a person must judge: a `defaultPrompt` that only restates the plugin name, `capabilities` that disagree with actual contributions, an SVG with font text, and, under `--for-publish`, a missing `interface.websiteUrl`. Semantic copy rules in §5.2 have no mechanical check. The Agent applies them itself, and reviewers apply them to contributions.

Exit status is 0 when there are no errors and non-zero otherwise. Warnings do not change it. `--json` writes:

```json
{
  "isValid": false,
  "candidatePath": "/path/to/plugin",
  "pluginId": "review-tools",
  "version": "0.1.0",
  "forPublish": false,
  "diagnostics": [
    {
      "severity": "error",
      "code": "PluginIconMissing",
      "message": "interface.logo is not declared.",
      "path": ".craft-plugin/plugin.json"
    }
  ]
}
```

`code` is stable and `message` is English fallback text.

Verification is an authoring and publication gate. Discovery, `plugin/install`, and `plugin/installLocal` do not run it, and their acceptance rules stay as their owning specs define them.

## 7. Activation and acceptance

The Agent runs `dotcraft plugin verify`, fixes every error, and then makes the plugin active and exercises each contribution. Passing verification does not show that a plugin works.

| Contribution | Evidence of success |
|---|---|
| Skills, Commands | The plugin Skill loads with `SkillView`, and a realistic request follows its workflow |
| MCP servers | The server starts, its tools are listed, and one harmless call succeeds |
| Hooks | After the user grants trust, the event fires and its effect is observed |
| Dynamic Workflows | The workflow is listed and a bounded run completes |
| LSP servers | The server starts for a matching file and returns one result |
| App descriptors | The app appears and its connection flow starts |
| Desktop extension | The build succeeds, and the view opens or the action runs in Desktop |
| .NET extension | `DotNetPlugin.Build` succeeds, and a contributed Tool runs on a later Turn |
| Settings | The field appears and its value reaches the plugin |

Some steps belong to the user, such as refreshing the plugin list, granting trust, or starting a new thread. The Agent names the exact step and marks the result unverified until it has evidence. It never reports success from file creation or installation alone.

The completion report states the plugin location, its contributions, the verification result, what was and was not exercised, and how to try it. It may mention in one line that the plugin can be shared.

## 8. Updating a plugin

The Agent inspects the current plugin and edits the source that owns the change:

| Plugin | Where the change happens |
|---|---|
| Workspace-local or user-owned | In place |
| .NET under development | The project's `src/`, followed by `DotNetPlugin.Build` |
| Published from another repository | That repository, through its release process |
| Managed copy from a marketplace or built-in catalog | Not in place, because a refresh replaces it. The user changes the source, or adapts an unmarked copy in the workspace-local root with the same id, which takes precedence by root priority |

Rules:

- The plugin `id` never changes once anyone else has the plugin. A new id is a new plugin.
- Existing metadata, version scheme, and contributions stay unless the request changes them.
- `version` increases when a changed plugin is distributed: patch for fixes, minor for added behavior, and major for incompatible changes.
- The Agent reruns verification and exercises the affected contributions (§7).

A question about a plugin does not by itself call for an edit.

## 9. Sharing and contribution

Sharing starts when the user asks for it. The Agent first establishes the audience:

| Audience | Route |
|---|---|
| People working in this workspace's repository | Commit `.craft/plugins/<id>` with the workspace |
| A team, an organization, or a public audience outside official review | A marketplace repository the author controls ([Plugin Registry](../architecture/plugin-registry.md) §4 and §5) |
| Every DotCraft user | A contribution to the official registry, under [Plugin Registry](../architecture/plugin-registry.md) §11 |

Contribution steps for the official registry:

1. Confirm the plugin passed §7 and is meant for a public audience.
2. Run `dotcraft plugin verify --for-publish` and fix every error.
3. Fork the registry and create a branch. Copy the plugin to `plugins/<id>/`, leaving out development artifacts, and add its marketplace entry.
4. Prepare the pull request from what the plugin actually does: its purpose, contributions, what was tested and on which DotCraft version, what data and network access it uses, the origin and license of its code and icon, and, for compiled or bundled code, the public source repository and commit.
5. Stop and show the prepared branch and pull request text.

Forking, pushing, and opening a pull request are outward-facing. Each requires the user's explicit request in the conversation. If the GitHub CLI or sign-in is unavailable, the Agent gives the user the exact commands and text instead.

The Agent reports the state actually reached: verified locally, branch prepared, pull request opened, or merged.

## 10. Evaluation

`plugin-creator` is checked with behavioral runs when its instructions change. Evaluation assets do not ship with the built-in Skill and do not run on every pull request.

A run uses [`dotcraft exec`](exec-cli.md) in a temporary workspace with no DotCraft source access. It gets a realistic user request and no hints.

| Scenario | Passes when |
|---|---|
| Turn a workflow from the conversation into a Skill plugin | Verification passes, icons exist, and the Skill reflects the workflow |
| Wrap an MCP server | Verification passes, and the tools are listed and called once |
| Create a .NET plugin with a Desktop view | Verification passes on the built bundle with `interface` and icons present |
| Change an existing plugin | The id is unchanged, the version increased, and the change works |
| Prepare a contribution | `--for-publish` passes, and nothing was pushed without a request |
| Ask for a one-project Skill | `plugin-creator` is not selected and `skill-creator` is |
| Ask to install a market plugin | `plugin-creator` is not selected |

Assertions check outcomes: the verify result, resulting files, and observed tool behavior. They do not check instruction wording or the order of tool calls.

A substantive rewrite runs every scenario three times against the previous Skill version as a baseline. A narrow change runs the affected scenarios once. The change review records the model, configuration, and results, including anything untested.

During authoring, an independent test run of a plugin Skill is optional. It is worthwhile when a Skill is complex or risky, requires the user's agreement when it costs time or money, and uses an isolated temporary workspace.

## 11. Invariants

- Authoring never lowers discovery, installation, or trust requirements.
- `plugin-creator` carries workflow, gates, and templates. Public documentation owns contribution APIs and the manifest field reference.
- Instructions work without a DotCraft source checkout, and any documentation link points at the public site.
- Verification runs no plugin code.
- The Agent does not publish, push, or open a contribution without the user's explicit request.
