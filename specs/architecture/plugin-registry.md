# DotCraft Plugin Registry Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Draft |
| **Date** | 2026-06-28 |
| **Related Specs** | [Plugin Architecture](plugin-architecture.md), [AppServer Protocol](../protocols/appserver-protocol.md), [App Binding](../protocols/app-binding.md) |

Purpose: define the product and process contract for curated DotCraft plugin registries. A registry lets DotCraft discover installable external integration plugins without bundling every integration in the DotCraft Desktop release package.

---

## 1. Overview

A DotCraft plugin registry is a curated source repository. It contains:

- a marketplace index at `.craft/plugins/marketplace.json`;
- one plugin source directory per listed plugin, normally under `plugins/<pluginName>/`;
- CI validation that ensures marketplace entries point to valid plugin roots inside the repository.

The registry follows the same broad model as a source-based marketplace: the index controls discovery and ordering, while each plugin directory remains the source bundle that DotCraft installs. Registry entries do not define plugin runtime contributions. Skills, app descriptors, MCP/LSP descriptors, Desktop extensions, assets, and path metadata are read from the plugin's `.craft-plugin/plugin.json` and referenced files.

Installing a registry plugin copies the verified registry plugin directory into the workspace at `.craft/plugins/<pluginName>`. DotCraft loads plugin contributions only after local installation.

---

## 2. Goals

- Decouple optional external integration plugins from DotCraft Desktop release packaging.
- Keep availability-critical DotCraft plugins bundled with the main Desktop package.
- Provide an official curated source registry for first-party installable plugins.
- Allow users and organizations to append additional registries, including internal mirrors or company registries.
- Preserve the existing local plugin runtime model and plugin manifest contract.

---

## 3. Non-goals

- The registry is not a marketplace billing, rating, telemetry, or ranking system.
- The registry does not replace `.craft-plugin/plugin.json`.
- The registry does not load plugin code directly from a network URL.
- The registry does not install native applications required by plugin integrations.
- v1 does not require per-plugin release ZIPs, package hashes, or per-plugin publish automation.

---

## 4. Repository Contract

The default official registry repository is a source registry. Each approved plugin lives under a repository-local directory and must contain exactly one DotCraft plugin root with `.craft-plugin/plugin.json`.

The marketplace document has this v1 shape:

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

- `name` identifies the plugin entry and must match the plugin manifest `id`.
- `source.source` must be `local`. v1 does not support marketplace entry sources such as `url` or `git-subdir`.
- `source.path` must be a relative path beginning with `./`, must not contain `..`, and must stay inside the registry snapshot.
- The target directory must contain `.craft-plugin/plugin.json`.
- `policy.installation` controls whether the plugin is shown as installable, is required, and must be explicitly set to `AVAILABLE` in v1.
- `policy.authentication` describes when the plugin expects authentication or app connection setup; v1 supports `ON_INSTALL`.
- Runtime contribution metadata must stay in the plugin manifest and descriptor files, not in the marketplace entry.

---

## 5. Registry Sources

DotCraft may discover registries from multiple sources:

- a host-provided default official registry URL;
- additional configured sources in `Plugins.PluginRegistries`;
- environment-provided sources for deployment-specific overrides.

A registry source points to an HTTPS repository snapshot archive, a local archive file, or a local registry directory. The default marketplace path is `.craft/plugins/marketplace.json`; configured sources may override that path.

Users and organizations may disable the default official registry with `Plugins.DisableDefaultPluginRegistry`. This supports private or internal-only deployments where only company-managed registries should be used.

Registry source failures are non-fatal. DotCraft should use a recent cached snapshot when available. If no registry source can be loaded, local workspace plugins and bundled built-ins remain available.

---

## 6. Precedence

Plugin discovery precedence is:

1. Workspace-local plugins under `.craft/plugins`.
2. Explicit roots in `Plugins.PluginRoots`.
3. User-global plugins.
4. Desktop-bundled built-in plugins.
5. Registry plugins, in source order.

Higher-priority sources win. Duplicate plugin ids from lower-priority sources are skipped with diagnostics. If an organization needs to replace an official registry plugin with a same-id internal plugin, it should disable the default registry and configure the internal registry source explicitly.

---

## 7. Install Lifecycle

DotCraft clients may present registry entries as installable catalog items before installation. An uninstalled registry plugin does not contribute skills, tools, apps, MCP/LSP servers, or Desktop extensions.

Installing a registry plugin must:

- load a registry snapshot from cache or source;
- resolve the marketplace entry to a repository-local plugin directory;
- validate the plugin manifest id against the marketplace entry name;
- copy the plugin directory to `.craft/plugins/<pluginName>`;
- write a managed marker for DotCraft-owned refresh/removal behavior;
- refresh plugin-contributed skills, apps, MCP/LSP servers, and Desktop extension metadata through the normal plugin runtime.

User-owned workspace plugins without a managed marker must not be overwritten by registry install or refresh behavior.

---

## 8. Security and Trust Boundaries

The registry is a curated trust boundary, not a sandbox.

- DotCraft must not execute plugin code directly from the registry URL.
- DotCraft must reject marketplace paths that escape the registry snapshot.
- DotCraft must reject plugins whose manifest id does not match the marketplace entry name.
- Registry review should check misleading metadata, missing referenced files, unsupported contribution types, and obvious provenance problems.
- Desktop extensions remain trusted installed plugin code and load only after installation and enablement.
- App Binding, MCP/LSP, dynamic tools, and Desktop extension permissions remain governed by their existing specs and runtime consent flows.

---

## 9. Publication Workflow

The intended publishing flow is:

1. A contributor updates or adds a plugin directory under `plugins/<pluginName>/`.
2. The contributor updates `.craft/plugins/marketplace.json`.
3. CI validates marketplace shape, path safety, manifest id consistency, and referenced files.
4. DotCraft maintainers review and merge the registry pull request.
5. The merged default branch becomes available through registry snapshot refresh.

---

## 10. Acceptance Checklist

- Optional external integration plugins can be discovered from a source registry instead of the Desktop release package.
- Availability-critical bundled plugins remain available without network access.
- Registry entries point only to repository-local plugin directories.
- Installed registry plugins use the normal local plugin lifecycle.
- Multiple registry sources and default-registry disablement are supported.
