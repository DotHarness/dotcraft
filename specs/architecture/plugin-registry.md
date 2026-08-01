# DotCraft Plugin Registry Specification

| Field | Value |
|-------|-------|
| **Version** | 0.4.0 |
| **Status** | Draft |
| **Date** | 2026-08-02 |
| **Related Specs** | [Plugin Architecture](plugin-architecture.md), [AppServer Protocol](../protocols/appserver-protocol.md), [App Binding](../protocols/app-binding.md) |

Purpose: define the product and process contract for DotCraft plugin marketplaces. A marketplace lets DotCraft discover installable external integration plugins without bundling every integration in the DotCraft Desktop release package, and lets users and organizations add their own plugin sources.

---

## 1. Overview

A DotCraft plugin marketplace is a curated source repository. It contains:

- a marketplace index at `.craft/plugins/marketplace.json`;
- one plugin source directory per listed plugin, normally under `plugins/<pluginName>/`;
- CI validation that ensures marketplace entries point to valid plugin roots inside the repository.

The index controls discovery and ordering, while each plugin directory remains the source bundle that DotCraft installs. Marketplace entries do not define plugin runtime contributions. Skills, app descriptors, MCP/LSP descriptors, Desktop extensions, assets, and path metadata are read from the plugin's `.craft-plugin/plugin.json` and referenced files.

Installing a marketplace plugin copies the verified plugin directory into the workspace at `.craft/plugins/<pluginName>`. DotCraft loads plugin contributions only after local installation.

Marketplace sources are recorded once for the user and are then available in every workspace. Plugin installation stays per workspace.

---

## 2. Goals

- Decouple optional external integration plugins from DotCraft Desktop release packaging.
- Keep availability-critical DotCraft plugins bundled with the main Desktop package.
- Provide an official curated source marketplace for first-party installable plugins.
- Let users and organizations add additional marketplaces, including private mirrors and company-internal repositories, without editing configuration files by hand.
- Preserve the existing local plugin runtime model and plugin manifest contract.

---

## 3. Non-goals

- The marketplace is not a billing, rating, telemetry, or ranking system.
- The marketplace does not replace `.craft-plugin/plugin.json`.
- DotCraft does not load or execute plugin code directly from a remote source.
- The marketplace does not install native applications required by plugin integrations.
- DotCraft does not store, prompt for, or manage source credentials. Authentication is delegated to the host version control configuration.

---

## 4. Marketplace Document Contract

Each marketplace must contain exactly one marketplace document, and every plugin it lists must be a repository-local directory containing `.craft-plugin/plugin.json`.

The marketplace document has this shape:

```json
{
  "name": "dotcraft-official",
  "interface": {
    "displayName": "DotCraft Official Plugins"
  },
  "plugins": [
    {
      "name": "example-plugin",
      "source": {
        "source": "local",
        "path": "./plugins/example-plugin"
      },
      "policy": {
        "installation": "AVAILABLE",
        "authentication": "ON_INSTALL"
      },
      "category": "Productivity"
    }
  ]
}
```

Rules:

- The document `name` is the identity of the marketplace. DotCraft derives the configuration key and the installed root directory name from it, not from user input.
- The document `name` must be a single safe path segment. Names that cannot be used as a directory name are rejected.
- Plugin entry `name` identifies the plugin entry and must match the plugin manifest `id`.
- Plugin entry `source.source` must be `local`; entry sources such as `url` or `git-subdir` are not supported.
- Plugin entry `source.path` must be a relative path beginning with `./`, must not contain `..`, and must stay inside the marketplace root.
- The target directory must contain `.craft-plugin/plugin.json`.
- `policy.installation` controls whether the plugin is shown as installable and must be explicitly set to `AVAILABLE`.
- `policy.authentication` describes when the plugin expects authentication or app connection setup; `ON_INSTALL` is supported.
- Runtime contribution metadata must stay in the plugin manifest and descriptor files, not in the marketplace entry.

---

## 5. Marketplace Source Kinds

A marketplace source declares where the marketplace document and its plugin directories come from.

| Kind | Source value | Fetch | Materialized |
|------|--------------|-------|--------------|
| `git` | Repository URL or shorthand | Version control checkout | Yes, under the installed marketplace root |
| `local` | Directory path on this machine | None | No, read in place |
| `archive` | HTTPS archive URL, or a local archive file | Archive download and extraction | Yes, in a content-addressed cache |

`archive` is the pre-existing source kind and remains supported for configured sources and the host-provided default marketplace. Entries that omit the source kind keep the pre-0.3.0 behavior: an existing directory or archive file path is treated as a local snapshot, and any other value is treated as an archive URL.

### 5.1 Accepted git source syntax

| Input | Resolved |
|-------|----------|
| `owner/repo` | `https://github.com/owner/repo.git` |
| `owner/repo@ref` | `https://github.com/owner/repo.git` at `ref` |
| `https://host/team/repo.git` | as given |
| `https://host/team/repo.git#ref` | as given, at `ref` |
| `ssh://git@host/team/repo.git` | as given |
| `git@host:team/repo.git` | as given |

An explicit reference parameter overrides a reference embedded in the source string. When no reference is given, the source default branch is used.

### 5.2 Local sources

A local source must resolve to an existing directory containing a valid marketplace document. A path that resolves to a file is rejected. `file://` URLs are not accepted as a source; local sources are filesystem paths.

### 5.3 Sparse paths

A git source may declare sparse paths so that only the marketplace document and the plugin directories a user needs are checked out. Each sparse path must be repository-relative, must not be absolute, and must not contain `..`. Sparse paths are valid only for git sources; declaring them on any other kind is rejected.

---

## 6. Installed Marketplace Roots

Git marketplaces are materialized under a user-global installed marketplace root, one directory per marketplace name. Local marketplaces are never copied; DotCraft reads the user's directory in place. Archive marketplaces keep their existing content-addressed snapshot cache.

Rules:

- The directory name is derived from the marketplace document `name` by a safe-name transform. A name that reduces to an empty or traversing segment is rejected.
- The resolved directory must stay inside the installed marketplace root.
- A fetch stages into a temporary directory inside the installed marketplace root and replaces the destination only after validation succeeds, so a failed fetch never leaves a partially updated marketplace.

---

## 7. Marketplace Lifecycle

### 7.1 Add

1. Parse and normalize the source into a source kind, a source value, an optional reference, and sparse paths.
2. Reject sparse paths on non-git sources, and reject an explicit reference on non-git sources.
3. If a configured entry already matches the same kind, source, reference, and sparse paths, and its root still contains a valid marketplace document, report the marketplace as already added and do not fetch.
4. For a local source, validate the directory in place and read the marketplace name from it.
5. For a git source, fetch into a staging directory, validate the marketplace document, and read the marketplace name from it.
6. Reject a marketplace whose name is already configured from a different repository or directory. The existing marketplace must be removed first. Re-adding the same repository or directory at another reference or sparse path set re-points the existing marketplace instead of failing.
7. For a git source, atomically replace the installed root for that name with the staged content.
8. Record the entry in user-global configuration, including the resolved source, reference, sparse paths, the update timestamp, and the resolved revision when the source kind provides one.
9. Refresh plugin discovery so the marketplace plugins appear as installable catalog entries.

Adding a marketplace never installs a plugin.

### 7.2 Fetch requirements

- A git fetch requires a version control executable available on the host. When it is unavailable, add and refresh fail with a stable error rather than silently degrading to another transport.
- Fetches run non-interactively. A source that would require an interactive credential or host verification prompt fails instead of blocking the server.
- Fetches are bounded by a timeout and are cancellable.
- When sparse paths are declared, the fetch avoids downloading file contents outside the requested paths.

### 7.3 Refresh

Refresh re-fetches one marketplace or every configured marketplace. Git marketplaces are re-fetched at their configured reference, local marketplaces are re-validated in place, and archive marketplaces re-download their snapshot. A failure for one marketplace is reported for that marketplace and does not fail the others.

### 7.4 Remove

Removing a marketplace deletes its configuration entry and, for materialized kinds, deletes its installed root. Removal does not uninstall plugins that were already installed into a workspace: those are workspace-owned copies under `.craft/plugins/<id>` and remain until removed through the ordinary plugin removal flow.

### 7.5 Discovery never fetches

Plugin discovery runs on every plugin listing and every plugin mutation. Discovery reads only materialized roots, local source directories, and cached archive snapshots that already exist on disk. Discovery must never start a version control fetch. Archive sources retain their existing bounded, non-fatal cached refresh; every other fetch happens only through an explicit add or refresh operation.

### 7.6 Archive cache lifecycle

Archive marketplaces use the user-global cache under `<craft-home>/cache/plugin-registries`. The marketplace document `name` is the stable cache identity, and only one successfully activated archive snapshot is retained for that identity.

An archive refresh must extract into a temporary directory, validate the marketplace document, and atomically activate the new snapshot before deleting an older snapshot. A failed download, extraction, validation, or activation leaves the previous snapshot available. After successful activation, DotCraft immediately removes other cache generations for the same marketplace identity on a best-effort basis; cleanup failure must not make the new snapshot unavailable. Removing an archive marketplace also removes its managed cache snapshots.

Each activated snapshot stores internal cache metadata containing a schema version, marketplace identity, source key, marketplace path, and update time. Existing snapshots without metadata remain readable: DotCraft derives their identity from the marketplace document, writes metadata when possible, and applies the same single-version pruning rule. Cache operations also remove interrupted archive staging directories older than ten minutes while leaving newer staging directories and unrelated files untouched.

The cache root must be derived from the effective Craft home supplied to the runtime or development resolver. Tests and alternate Craft homes must not write into the default user's cache. These cache mechanics do not apply to Git marketplace roots, local marketplaces, or plugins already installed into a workspace.

---

## 8. Precedence

Plugin discovery precedence is:

1. Workspace-local plugins under `.craft/plugins`.
2. Explicit roots in `Plugins.PluginRoots`.
3. User-global plugins.
4. Desktop-bundled built-in plugins.
5. Marketplace plugins, in source order.

Higher-priority sources win. Duplicate plugin ids from lower-priority sources are skipped with diagnostics. If an organization needs to replace an official marketplace plugin with a same-id internal plugin, it should disable the default marketplace and configure the internal marketplace explicitly.

Users and organizations may disable the host-provided default marketplace with `Plugins.DisableDefaultPluginRegistry`. This supports private or internal-only deployments where only organization-managed marketplaces should be used.

---

## 9. Plugin Install Lifecycle

DotCraft clients may present marketplace entries as installable catalog items before installation. An uninstalled marketplace plugin does not contribute skills, tools, apps, MCP/LSP servers, or Desktop extensions.

Installing a marketplace plugin must:

- resolve the marketplace entry to a marketplace-local plugin directory;
- validate the plugin manifest id against the marketplace entry name;
- copy the plugin directory to the workspace at `.craft/plugins/<pluginName>`;
- write a managed marker for DotCraft-owned refresh/removal behavior;
- refresh plugin-contributed skills, apps, MCP/LSP servers, and Desktop extension metadata through the normal plugin runtime.

User-owned workspace plugins without a managed marker must not be overwritten by marketplace install or refresh behavior.

---

## 10. Security and Trust Boundaries

A marketplace is a curated trust boundary, not a sandbox. Adding a marketplace is an explicit user decision to trust that source.

Source validation:

- Source values are restricted to the syntax in Section 5.1 and to local directory paths. Any other scheme, including `file://`, is rejected.
- Version control transports that can execute an arbitrary command as a remote helper are disabled for marketplace fetches, and a source that would invoke one is rejected before any fetch runs.
- Source values carrying embedded credentials are rejected. DotCraft never stores credentials and relies on the host version control credential configuration.
- Sparse paths are validated as repository-relative paths without traversal.

Content validation:

- DotCraft must not execute plugin code from a marketplace source.
- DotCraft must reject marketplace plugin paths that escape the marketplace root.
- DotCraft must reject plugins whose manifest id does not match the marketplace entry name.
- The resolved installed root must stay inside the installed marketplace root.

Runtime boundaries:

- Adding a marketplace only makes its plugins visible as installable catalog entries. No plugin contributes runtime behavior until the user installs it into a workspace.
- Desktop extensions remain trusted installed plugin code and load only after installation and enablement.
- App Binding, MCP/LSP, dynamic tools, and Desktop extension permissions remain governed by their existing specs and runtime consent flows.

Review expectations for the official marketplace: check misleading metadata, missing referenced files, unsupported contribution types, and obvious provenance problems.

---

## 11. Publication Workflow

The intended publishing flow for the official marketplace is:

1. A contributor updates or adds a plugin directory under `plugins/<pluginName>/`.
2. The contributor updates `.craft/plugins/marketplace.json`.
3. CI validates marketplace shape, path safety, manifest id consistency, and referenced files.
4. DotCraft maintainers review and merge the marketplace pull request.
5. The merged default branch becomes available through marketplace refresh.

The same repository layout applies to any user-added or organization-managed marketplace.

---

## 12. Acceptance Checklist

- Optional external integration plugins can be discovered from a marketplace instead of the Desktop release package.
- Availability-critical bundled plugins remain available without network access.
- A user can add a marketplace from a repository URL, a shorthand, or a local directory without editing configuration files by hand.
- An added marketplace is recorded once for the user and is available in every workspace, while plugin installation stays per workspace.
- Marketplace entries point only to marketplace-local plugin directories.
- Installed marketplace plugins use the normal local plugin lifecycle.
- Plugin discovery never triggers a version control fetch.
- Multiple marketplaces and default-marketplace disablement are supported.
