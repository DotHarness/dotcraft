# Agent packages

| Field | Value |
|---|---|
| Version | 0.8.3 |
| Status | Draft |
| Date | 2026-10-06 |
| Parent spec | [Agent Profiles](agent-profiles.md) |

This specification owns how an Agent Profile leaves one DotCraft installation as a file and arrives in another: the file an export writes, what an import reads from it, and what the receiving installation installs before the profile exists. [Agent Profiles](agent-profiles.md) owns the profile document and its sources. Universe reads and writes the same file, so an Agent moves between DotCraft and Universe in either direction.

## Scope

What makes an Agent the same Agent elsewhere is its Profile document and the skills and plugins that document relies on. Everything else belongs to the installation: provider credentials, MCP server configuration, conversations, memory, and channel bindings. An export carries the first and none of the second.

This contract does not define importing over an existing profile, exporting several profiles at once, or carrying MCP server configuration.

## The Agent package

An Agent package is one zip, named `<slug>.agent.zip`, where the slug is the lowercase ASCII letters and digits of the profile name joined by single hyphens, at most 80 characters, or `agent` when nothing remains.

- `agent.json`, the manifest, whose `format` is `dotcraft-agent/1`. Its camelCase fields are `format`, `name`, `description`, `profile` (the document's path within the zip), `exportedAt`, and `packages`.
- The Profile document, `profile.md`, exactly as the exporting installation stores it.
- `packages/<kind>-<name>.zip` for every listed package that travels with it.

Each listed package states `kind` (`skill` or `plugin`), `name`, `displayName`, `version`, `sha256`, `dotnet`, the `skills` and `mcpServers` it provides, `marketplace` when it is installed from one, and `file` when its zip travels in the package. `mcpServers` lists a plugin's own server names; a document names one as `<pluginId>:<server>`. `sha256` is the digest of the embedded zip and is empty for a marketplace reference. A skill package is a zip whose root holds `SKILL.md`; a plugin package is a zip whose root holds `.craft-plugin/plugin.json`. A package from a git or archive marketplace names that marketplace (`name`, `sourceKind`, `source`, `ref`, `marketplacePath`, `sparsePaths`) and travels without its bytes. Any other package travels as a zip.

A file whose first bytes are not a zip signature is a Profile document alone and imports as one.

An Agent package is at most 64 MiB including embedded packages. Each embedded package follows the bounds of its kind: a skill at most 1000 files and 100 MiB expanded, a plugin at most 10,000 files and 512 MiB expanded. The manifest is at most 1 MiB and the document at most 64 KiB of UTF-8.

## Export

Export is available for created user and workspace profiles. Built-in, plugin, and managed profiles are not exported.

- The document is the stored profile file.
- The offered packages are the skills of the user and workspace skill roots and the installed plugins, each once by kind and name. Built-in skills, plugin skills, and plugins that the installation's bundled or host-provided plugin sources offer are not offered, because every installation has them.
- A package starts included when the document relies on it: a skill the document preloads or allows, a plugin providing such a skill, a plugin whose MCP server the document names (`<pluginId>:<server>`), a plugin the document's plugin policy allows, or a plugin that registers a tool the document allows by its model-visible name. The user may include or leave out any package.
- A plugin that a configured git or archive marketplace offers under the same id travels as that marketplace reference. Every other included package travels as its zip.
- MCP servers configured in settings never travel, because their configuration can hold credentials. The import lists the names the document uses that nothing provides.
- An export whose file exceeds the bound, or that the import rules would refuse, is refused, and the user can leave a package out. Every written file is read back with the import rules before it is returned.
- Exporting changes nothing in the installation.

## Import

Import starts from the Agents page.

1. The user chooses or drops a `.zip` or `.md` file. The client uploads it in chunks. The server reads it whole before keeping it: an entry that leaves the zip, a manifest of another format, a listed package whose file is missing or is not the package it claims to be, or a document over the bound is refused, and nothing is kept. A readable file is held for one hour for the uploading connection, and is discarded when that connection closes.
2. The server answers with a preview: the profile name and whether a user or workspace profile already has it, the description, document diagnostics, each listed package's state, and the names the document uses that neither the installation nor the file provides.
   - `installed`: an installed skill or plugin has the same kind and name, whatever its version. It is left as it is.
   - `bundled`: the file carries it. It installs from the file.
   - `marketplace`: a configured marketplace with the same source offers it. It installs from that marketplace.
   - `addMarketplace`: it comes from a git marketplace that is not configured. The marketplace is added, then the plugin installs from it.
   - `unavailable`: a local-directory marketplace, which exists only on the exporting machine (`localMarketplace`); a configured marketplace that no longer offers it; or an archive marketplace that is not configured, because archive registries come from the host's configuration and are never added from a file (`notOffered`).
3. The user chooses the name, the description, the save location (user or workspace), and which installable packages to install.
4. Committing installs the chosen packages, then creates the profile from the document under the chosen name in the chosen location and opens it in the editor. Bundled skills and plugins install into the chosen location; marketplace plugins install the way the Plugins page installs them. A plugin carrying .NET code installs untrusted, as every newly installed plugin does; trusting it stays with the plugin's own trust step. A name already used by a user or workspace profile, an expired import, a document that does not validate under the chosen name and description, or a chosen package the import does not list as installable is refused before anything is installed. A chosen package that is already installed is skipped. A package that installed before a later refusal stays installed: it now belongs to the installation, not to the profile.

Previewing changes nothing in the installation. Commit rewrites only the document's front-matter `name` and `description` to the chosen values.

## Protocol

Methods belong to the `agent/profiles` group.

| Method | Purpose |
|---|---|
| `agent/profiles/export/plan` | `{id, source}` → the file name, the bound, and each offered package with its kind, name, display name, version, `.NET` flag, expanded size, marketplace name, and the reasons the document relies on it. |
| `agent/profiles/export/read` | `{id, source, packages, offset}` → `{totalBytes, dataBase64}`. Offset `0` writes the package for the chosen `{kind, name}` list and returns the first chunk; later offsets continue it; a negative offset discards it. Chunks are at most 1 MiB. |
| `agent/profiles/import/upload` | `{importId?, fileName, totalBytes, offset, dataBase64}` → `{importId, receivedBytes, preview?}`. The first chunk omits `importId`. The preview is returned with the last chunk. Chunks are at most 1 MiB. |
| `agent/profiles/import/commit` | `{importId, name, description?, source, packages}` → the created profile entry. |
| `agent/profiles/import/discard` | `{importId}` → `{}`. |

Errors are invalid-params errors whose `data.code` is one of the stable codes `agentPackageInvalid`, `agentPackageTooLarge`, `importExpired`, `agentNameTaken`, and `packageNotFound`; a document that does not validate returns the Agent Profile validation error. Clients localize them.

## Desktop

- The Agents page toolbar carries Import beside New agent. Import opens a dialog that accepts a dropped or chosen `.zip` or `.md`, states the bound, and warns that a shared Agent can carry skills and plugins that run on this machine.
- The preview step shows the name-derived avatar with the editable name, the description, the save location, the packages with one switch per installable package and the state of the others, and one warning line for unresolved names. Import stays disabled while the name is empty or taken or the document has errors. Choose another file discards the staged import.
- Success closes the dialog and opens the imported profile in the editor. Each installed plugin that carries .NET code then opens its plugin dialog at the trust step, one after another.
- The editor's More actions menu carries Export… for created user and workspace profiles. Export opens a dialog listing the offered packages with switches and their sizes, and a note that MCP server settings are not included. The server enforces the bound when it writes the file. Export writes the file through the system save dialog.

## Acceptance scenarios

- A profile exported with one workspace skill and one marketplace plugin imports into an installation holding neither but holding that marketplace: both install and the profile opens in the editor.
- Importing the same file again lists both packages as installed, installs nothing, and refuses the taken name until the user renames it.
- A Profile document alone imports under the chosen name and lists the skills it names that nobody provides.
- A zip with an entry outside it is refused and creates nothing.
- A .NET plugin in an import installs untrusted and its plugin dialog asks for trust after the import; an expired import is refused.
- A file exported by Universe imports into DotCraft, and a file exported by DotCraft imports into Universe.
