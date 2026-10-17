# DotCraft Configuration Specification

| Field | Value |
|-------|-------|
| **Version** | 0.8.10 |
| **Status** | Living |
| **Date** | 2026-10-17 |

Purpose: Define how DotCraft configuration fields are declared, layered, read, written, applied at runtime, and exposed to AppServer clients.

## 1. Scope

This specification covers the root `AppConfig`, its `[ConfigSection]` sections (including module sections stored in extension data), the user and workspace configuration files, the configuration descriptor registry, the configuration service in Session Core, and the AppServer methods and notification that read and change configuration. Model provider definitions (`Providers`) are configuration and follow this specification.

Domain operations that persist configuration as a side effect keep their own methods and semantics: thread settings (`thread/config/update`), skills, plugins, MCP servers, hooks, external channels, SubAgent profiles, and source control. They are out of scope except where they share the change notification. Subscription sign-in (`auth/openai/login`, `auth/openai/logout`) persists its provider binding through the configuration service (§6.6).

## 2. Goals

- A configuration field is declared once, as a C# property with `[ConfigField]` metadata, and needs no further wiring to be readable, writable, validated, applied, and reported to clients.
- Clients read and change configuration through generic key-path methods, so adding a field never changes the wire contract.
- Reload metadata describes real behavior: what a field says about how it takes effect is what the runtime does.
- Clients read the configuration of the server they are connected to, never local files, regardless of how the connection was established.
- Secrets can be set and replaced through configuration writes but never read back.

## 3. Declaration and descriptor registry

A field is a public property of `AppConfig` or of a class marked `[ConfigSection]`. `[ConfigField]` supplies display name, hint, sensitivity, numeric bounds, options, reload behavior, and subsystem key; a section's `[ConfigSection]` supplies its key path and default reload behavior.

The build generates one descriptor per field from these attributes. A descriptor carries:

- the key path: the dot-joined on-disk property names from the root, for example `InstantInterruptEnabled` or `Tools.CodeMode.Mode`;
- the value type, the default value, bounds and options, and sensitivity;
- the effective reload behavior (`Hot`, `SubsystemRestart`, or `ProcessRestart`) and subsystem key;
- typed accessors that read and assign the field on an `AppConfig` instance without reflection.

Fields marked `Ignore` produce no descriptor. Collection sections keyed by a root key (`RootKey`) are not addressable field by field; they are written through their domain methods.

The configuration schema returned to clients (`config/schema`) is generated from the same attribute metadata as the descriptors.

A value is sensitive when its field is marked sensitive or its key names a secret: a key ending in `ApiKey`, `Token`, `Secret`, or `Password`, an `Authorization` key, or any value of a `Headers`, `EnvironmentVariables`, or `Env` map, at any depth, including inside object-valued fields such as `Providers.<id>.ApiKey`.

## 4. Layers and effective configuration

Configuration has two file layers, from lowest to highest precedence:

| Layer | File |
|-------|------|
| `user` | The global configuration file, `~/.craft/config.json` unless the host supplies another path |
| `workspace` | `<workspace>/.craft/config.json` |

The effective configuration is the descriptor defaults overlaid by the user layer and then the workspace layer. Objects merge recursively by case-insensitive key; scalars and arrays are replaced by the higher layer. `ProviderPreferences` replaces per provider. Environment-variable expansion applies to the effective result.

Each layer has a version: `sha256:` followed by the hex digest of the layer's JSON serialized with sorted keys. A missing file has the version of an empty object.

For each field whose value comes from a file layer, the origin of its key path is that layer. Fields at their defaults have no origin.

## 5. Configuration service

Session Core owns one configuration service per workspace runtime. It is the only component that writes the user or workspace configuration file for field-level changes.

### 5.1 Read

A read returns the effective configuration, the origins map, and optionally each layer's own content and version. Non-empty sensitive values are masked as `***` in every returned value.

### 5.2 Write

A write applies one or more edits to a single target layer. Each edit names a key path, a value, and a merge strategy.

1. The target layer defaults to `workspace`, or to `user` when every edit is under `Providers` (§5.5). A target that is not one of the two file layers fails with `configLayerReadonly`.
2. When an expected version is supplied and differs from the target layer's current version, the write fails with `configVersionConflict`.
3. A key path that matches no descriptor and is not inside a descriptor-backed object fails with `configSchemaUnknownKey`.
4. Sensitive values are write-only. A `***` string that is the edit's value or the value of an object member nested in it is replaced by the value stored at the same path in the target layer as it was before the write, or removed when there is none, so writing the mask leaves the stored value unchanged. A `***` array element is kept as written. Any other value replaces the stored one.
5. A `null` value removes the key from the target layer and prunes parent objects left empty. `replace` sets the value; `upsert` deep-merges an object value into the existing object.
6. The edited layer is merged into a candidate effective configuration, which must deserialize into `AppConfig`; every field whose effective value changes must satisfy its descriptor's type, bounds, and options, and any semantic validator registered for an edited key path must pass. Values the write does not change are not re-validated, so an existing invalid value elsewhere does not block unrelated writes. Any failure rejects the whole write with `configValidationError` and leaves the file unchanged.
7. The file is replaced atomically under the same cross-process lock other configuration writers use.

The write result reports the target layer's new version and file path. When a higher-precedence layer still determines an edited field's effective value, the result status is `okOverridden` with the overriding layer and the masked effective value; otherwise it is `ok`.

A layer can also be replaced as a whole document, which is how the Dashboard saves its settings editor. Each `***` value in the document is restored as in step 4, so masked secrets survive a read-edit-save round trip. The replacement then follows steps 1, 6, and 7 and the rules of §5.5, with the semantic validators of every changed key path, and is applied and announced as in §5.3.

Results and error messages never contain a sensitive value.

### 5.3 Apply

After a write, the service recomputes the effective configuration and determines the changed key paths by comparing the previous and new effective values field by field. For each changed field it assigns the new value onto the runtime configuration instance through the field's accessor, so components holding that instance observe the change.

The field's reload behavior then decides when the change takes effect:

- `Hot`: no further action; readers observe the new value on their next read, and per-Turn readers from the next Turn.
- `SubsystemRestart`: the handler registered for the field's subsystem key runs once per write with the changed key paths of that subsystem.
- `ProcessRestart`: the value is stored and takes effect after the AppServer restarts.

Every `Hot` or `SubsystemRestart` field either is read at use time or has a registered subsystem handler. A field whose change cannot be honored without a restart is declared `ProcessRestart`.

Finally the service raises one change event with the write's source and the changed key paths. A write that changes no effective value raises no event. The event carries key paths only, never values.

### 5.4 Subsystems and semantic rules

The workspace runtime registers these subsystem handlers:

| Subsystem key | Effect |
|---------------|--------|
| `threadAgents` | Cached thread agents are rebuilt before their next Turn; a running Turn keeps its agent. |
| `lsp` | LSP servers reinitialize from the effective configuration. |
| `dreams` | The Dreams scheduler starts when Dreams and memory are both enabled and stops otherwise. |
| `skills` | Disabled skills are reapplied to the skills loader, the skills context is marked dirty, and thread agents are rebuilt. |

Semantic rules registered by the runtime:

- `Dreams.Interval` must be positive.
- Each `ProviderPreferences` entry the write changes must name a model, and when its provider and model resolve, its reasoning selection must be supported by that model. The write never rewrites a preference; a preference that becomes unsupported later, for example after a catalog change, is normalized when a thread captures it.
- The provider rules of §5.5.

Option sets are declared on the field: `Permissions.DefaultApprovalPolicy` accepts `default` and `autoApprove`, and enum fields such as `Tools.CodeMode.Mode` accept their member names case-insensitively.

### 5.5 Providers

`Providers` is an object-valued field mapping a provider id to a provider object with the fields `DisplayName`, `Protocol`, `ApiKey`, `AuthMethod`, `ChatGptAccountId`, `ChatGptPlanType`, `EndPoint`, `NetworkTimeoutSeconds`, `MaxOutputTokens`, `StreamMaxRetries`, `StreamIdleTimeoutMs`, `SupportsImageGeneration`, and `SupportsFreeformTools`. Provider ids compare case-insensitively. Its reload behavior is `SubsystemRestart` in the `threadAgents` subsystem.

Providers are created, edited, and deleted only by configuration writes: an edit of `Providers.<id>` or `Providers.<id>.<Field>` creates or changes a provider, and a `null` value at `Providers.<id>` deletes it. A write does not distinguish creating from editing; a client that must not overwrite an existing provider checks the listed ids and supplies the expected version of the user layer.

Provider definitions are personal and live in the user layer. A write whose edits are all under `Providers` targets the user layer by default; a write that mixes `Providers` edits with other keys must name the user layer; any write with an edit under `Providers` whose target is another layer fails with `configValidationError`, as does a replacement of the workspace layer that changes content under `Providers`.

While a remote model service manages providers, a write with any edit under `Providers` fails with `configValidationError`, and no configuration write replaces the service-managed provider set.

Semantic rules, each failing with `configValidationError`:

- Each provider entry the write changes has an id that is non-empty and contains no `/`, `\`, or `:`; a `Protocol`, when set, of `openai-chat-completions`, `openai-responses`, or `anthropic`, case-insensitively; an `EndPoint`, when non-empty, that is an absolute URI; positive `NetworkTimeoutSeconds`, `MaxOutputTokens`, and `StreamIdleTimeoutMs`; a `StreamMaxRetries` from `0` to `100`; and an `AuthMethod`, when set, of `apiKey` or `chatgptOAuth`, where `chatgptOAuth` requires an OpenAI protocol.
- After a write that edits `ProviderId` or anything under `Providers`, a non-empty effective `ProviderId` names a provider defined in the candidate configuration, or, while a remote model service manages providers, a provider that service manages, because the runtime then uses only the service-managed set. A write that leaves an already unresolved `ProviderId` unchanged is not rejected for it. Deleting or renaming the selected provider therefore fails.

Defaults are resolved when the configuration is applied, not written: a missing `DisplayName` is the provider id, a missing `Protocol` is `openai-chat-completions`, protocol spelling is canonicalized, a missing `AuthMethod` is `apiKey`, and a missing `SupportsImageGeneration` or `SupportsFreeformTools` is `true` for `chatgptOAuth` providers and API-key providers on the official OpenAI endpoint and `false` otherwise. The file keeps exactly what was written, and `config/read` reports stored values.

Applying a change to `Providers` replaces the runtime provider set, so new model requests, catalog requests, and provider tests use the new definitions, and rebuilds thread agents before their next Turn. Its change event lists the key path `Providers`.

## 6. AppServer contract

Configuration methods are available when the server advertises `capabilities.workspaceConfigManagement`.

### 6.1 `config/read`

Params: `includeLayers` (boolean, optional, default `false`).

Result:

- `config`: the effective configuration object, keyed by on-disk property names, with sensitive values masked.
- `origins`: an object mapping each key path whose value comes from a file layer to `{ name, version }`.
- `layers`: present when `includeLayers` is `true`; the `user` then `workspace` layer, each `{ name, version, config }` with sensitive values masked.

A layer `name` is `{ "type": "user", "file": <path> }` or `{ "type": "workspace", "file": <path> }`.

### 6.2 `config/value/write`

Params: `keyPath` (string; a segment that contains `.`, `"` or `\` is written in double quotes with `\` escaping, for example `ProviderPreferences."openai.personal"`), `value` (any JSON, `null` removes, `"***"` keeps a stored sensitive value), `mergeStrategy` (`replace` or `upsert`), `filePath` (string or `null`; a layer `file` from `config/read`, defaulting to the workspace layer, or to the user layer when the key path is under `Providers`), `expectedVersion` (string or `null`).

Result: `status` (`ok` or `okOverridden`), `version`, `filePath`, and `overriddenMetadata` (`{ message, overridingLayer, effectiveValue }`) when overridden.

### 6.3 `config/batchWrite`

Params: `edits` (array of `{ keyPath, value, mergeStrategy }`), `filePath`, `expectedVersion`. The edits apply atomically to one layer with the same rules and result as `config/value/write`; when `filePath` is omitted and every edit is under `Providers`, the batch targets the user layer; a batch that mixes `Providers` edits with other keys must name the user layer in `filePath` or is rejected with `configValidationError`.

### 6.4 Errors

Write failures are JSON-RPC invalid-request errors whose `data.configWriteErrorCode` is `configLayerReadonly`, `configVersionConflict`, `configValidationError`, or `configSchemaUnknownKey`, with an English message. Provider rule failures, including a `Providers` edit aimed at the workspace layer or made while a remote model service manages providers, use `configValidationError`.

### 6.5 `config/changed`

The notification's `regions` lists the changed configuration key paths for configuration writes. A change that includes `Providers` also lists the `providers` domain tag. Domain operations that change persisted state outside field-level configuration (skills, plugins, MCP, hooks, external channels, SubAgent settings, source control) report their existing domain tags. Clients refresh configuration views when any key path under a prefix they display appears, refresh provider views on `providers`, and keep their domain refresh rules for other domain tags.

### 6.6 Provider views and sign-in

`provider/list` is the read view of providers: each configured provider, or with a remote model service each provider it manages, with resolved defaults, capability flags, and `hasApiKey` instead of its key. `provider/test` probes a saved provider or an unsaved draft. Neither writes configuration.

`auth/openai/login` and `auth/openai/logout` persist their provider binding as one configuration write to the user layer, with the method name as `source`. The binding is subject to the same validation, layer rule, application, and change notification as any other write.

## 7. Clients

Desktop reads configuration with `config/read` (including layers when a view distinguishes workspace overrides from user defaults) and writes with `config/value/write` or `config/batchWrite` against the server it is connected to, for local, SSH-managed, and manually configured remote connections alike. Defaults come from the effective configuration, not from client constants. A setting control applies its change optimistically, restores the previous value and reports an error when the write fails, and refreshes when `config/changed` names one of its key paths.

The provider editor lists providers with `provider/list` and saves with `config/batchWrite` on `Providers.<id>`: a new or edited provider writes its fields, an untouched secret is sent as `***` or left out, and deleting writes `null` to `Providers.<id>`.

## 8. Constraints

- Configuration has no field-specific request or response members, and providers have no mutation methods of their own.
- No component other than the configuration service writes a configuration file for a field-level change, and every configuration file write is atomic under the shared lock.
- Sensitive values never leave the server through configuration reads, write results, error messages, or change notifications.
- Provider definitions are written only to the user layer.
- Region key paths are the canonical key paths from the descriptor registry. Clients match them against configuration keys case-insensitively, because file layers may spell keys in any case, and do not invent region names.

## 9. Acceptance checklist

- Adding a boolean field with `[ConfigField]` makes it readable through `config/read`, writable through `config/value/write`, validated, applied according to its reload behavior, and reported in `config/changed`, with no change to the protocol contract or to server request handlers.
- A Desktop setting on a manually configured remote connection shows and changes the remote server's value.
- Writing an invalid value, an unknown key path, or with a stale expected version leaves the file unchanged and returns the matching error code.
- Writing a sensitive field such as `AppServer.WebSocket.Token` or `Providers.<id>.ApiKey` stores the new value; a following `config/read` shows `***`; writing `***` leaves the stored value unchanged; writing `null` removes it; no response or notification contains the value.
- A `config/batchWrite` on `Providers.<id>` without `filePath` creates the provider in the user layer, `provider/list` then lists it with resolved defaults, and a new Turn uses it; writing `null` to `Providers.<id>` deletes it.
- A `Providers` edit naming the workspace layer, made while a remote model service manages providers, with an unsupported protocol, an invalid endpoint, an out-of-range bound, or `chatgptOAuth` on a non-OpenAI protocol fails with `configValidationError` and leaves the file unchanged.
- Deleting the provider that the effective `ProviderId` selects, or writing a `ProviderId` that names no provider, fails with `configValidationError`.
- A `Providers` change emits `config/changed` listing `Providers` and `providers`.
- Subscription sign-in and sign-out update the provider binding in the user layer and emit `config/changed` with the auth method as `source`.
- A workspace override that hides a user-layer write reports `okOverridden`.
- Every `Hot` or `SubsystemRestart` field is read at use time or has a registered subsystem handler, and a test enforces it.
