# Channel Module integration

This guide is for developers embedding TypeScript external channel modules into a host — Desktop, a CLI tool, or any supervisor process — through the `@dotcraft/channel` module contract. The SDK and channel packages are source previews: build `sdk/typescript` and install the required local package directories before following this guide. See the [TypeScript SDK setup](../sdks/typescript).

## 1. Overview

The module contract gives hosts a stable boundary:

- Load metadata from `manifest`
- Create a runnable instance through `createModule(context)`
- Observe machine-readable lifecycle and errors
- Render config UX from `configDescriptors`
- Substitute module variants by `moduleId` while keeping runtime channel identity by `channelName`

Import only from the package root. Don't import package-internal files or infer behavior from the source layout.

## 2. Loading a module

Import from the package root only.

```typescript
import { configDescriptors, createModule, manifest } from "@dotcraft/channel-feishu";
import type { ModuleFactory, ModuleManifest } from "@dotcraft/channel";

const moduleManifest: ModuleManifest = manifest;
const moduleFactory: ModuleFactory = createModule;

console.log(moduleManifest.moduleId);
console.log(configDescriptors.length);
```

## 3. Discovering modules

A host can maintain a registry from an allowlist of package roots or `moduleId` mappings.

Recommended model:

1. Load known package roots.
2. Read each `manifest`.
3. Index by `moduleId`.
4. Maintain optional channel grouping by `channelName`.

The selection key is `moduleId`. Runtime channel identity remains `channelName`.

## 4. Creating and starting a module instance

Create `WorkspaceContext` explicitly and pass it to the module factory.

```typescript
import { createModule, manifest } from "@dotcraft/channel-feishu";
import type { ModuleInstance, WorkspaceContext } from "@dotcraft/channel";

const context: WorkspaceContext = {
  workspaceRoot: "F:/workspace/demo",
  craftPath: "F:/workspace/demo/.craft",
  channelName: manifest.channelName,
  moduleId: manifest.moduleId,
};

const instance: ModuleInstance = createModule(context);
await instance.start();
```

The host controls startup inputs. Pass the workspace context explicitly — a module does not rely on the current working directory to locate the workspace.

## 5. Observing lifecycle

Register status handlers before calling `start()` so no early transition is missed.

```typescript
import type { LifecycleStatus, ModuleError, ModuleInstance } from "@dotcraft/channel";

function mapStatusToHostAction(status: LifecycleStatus, error?: ModuleError): string {
  switch (status) {
    case "configMissing":
      return "Prompt user to create module config";
    case "configInvalid":
      return `Show config error: ${error?.message ?? "Invalid config"}`;
    case "starting":
      return "Show connecting state";
    case "ready":
      return "Mark module active";
    case "authRequired":
      return "Start interactive setup flow";
    case "authExpired":
      return "Prompt re-authentication";
    case "degraded":
      return "Show degraded warning";
    case "stopped":
      return "Mark module stopped";
  }
}

function observeLifecycle(instance: ModuleInstance): void {
  instance.onStatusChange((status, error) => {
    const action = mapStatusToHostAction(status, error);
    console.log(`[module-status] ${status} -> ${action}`);
  });
}
```

The host can query immediate state through `instance.getStatus()` and last error through `instance.getError()`.

## 6. Rendering config UI

If exported, `configDescriptors` can drive host config forms without package-internal schema parsing.

```typescript
import { configDescriptors } from "@dotcraft/channel-weixin";
import type { ConfigDescriptor } from "@dotcraft/channel";

type FormField = {
  key: string;
  label: string;
  required: boolean;
  inputType: "text" | "password" | "checkbox" | "number";
};

function toFormField(descriptor: ConfigDescriptor): FormField {
  if (descriptor.dataKind === "secret") {
    return { key: descriptor.key, label: descriptor.displayLabel, required: descriptor.required, inputType: "password" };
  }
  if (descriptor.dataKind === "boolean") {
    return { key: descriptor.key, label: descriptor.displayLabel, required: descriptor.required, inputType: "checkbox" };
  }
  if (descriptor.dataKind === "number") {
    return { key: descriptor.key, label: descriptor.displayLabel, required: descriptor.required, inputType: "number" };
  }
  return { key: descriptor.key, label: descriptor.displayLabel, required: descriptor.required, inputType: "text" };
}

const fields = configDescriptors.map(toFormField);
console.log(fields);
```

Have the host UI respect:

- `required` for validation
- `masked` and `dataKind: "secret"` for protected input display
- descriptor labels/descriptions as user-facing guidance

## 7. Interactive setup

Interactive setup is signaled by lifecycle status, not host-specific UI assumptions.

```typescript
import type { ModuleInstance } from "@dotcraft/channel";

function attachInteractiveSetupHandlers(instance: ModuleInstance): void {
  instance.onStatusChange((status, error) => {
    if (status === "authRequired") {
      console.log("Display QR path or setup prompt to user");
      return;
    }
    if (status === "authExpired") {
      console.log("Notify session expired and start re-auth flow");
      return;
    }
    if (status === "configMissing" || status === "configInvalid") {
      console.log(`Config action needed: ${error?.message ?? status}`);
    }
  });
}
```

The host decides the UI (Desktop panel, CLI prompt, dashboard notification). The contract only requires structured state signaling.

## 8. Stopping a module

Stop with `await instance.stop()` and treat `stopped` as terminal for that runtime instance.

Recommended host behavior:

1. Disable send/tool actions for this module instance.
2. Mark connection as offline.
3. Keep last structured error for diagnostics.

## 9. Variant substitution

Variant substitution lets hosts swap module implementations while preserving logical channel identity.

Selection model:

- choose implementation by `moduleId`
- keep runtime identity by `channelName`
- keep default config naming by channel conventions unless manifest explicitly differs

Example:

- Standard: `moduleId = "feishu-standard"`, `channelName = "feishu"`
- Enterprise: `moduleId = "feishu-enterprise"`, `channelName = "feishu"`

A host can switch variants by changing the selected `moduleId` without changing the host-facing integration model.

## 10. Adding new modules

A third-party package is loadable by the same model when it exports from package root:

- `manifest`
- `createModule`
- optional `configDescriptors`

Checklist for new module packages:

1. Implement the `@dotcraft/channel` module contract types.
2. Keep host integration on package-root exports only.
3. Provide machine-readable lifecycle and error transitions.
4. Validate config in module boundary code.
5. Include package tests and conformance tests.

This keeps first-party, enterprise, and partner modules interchangeable at the host boundary.

## Related docs

- [Channel adapters](../sdks/channels) — the adapter base class the modules build on.
- [TypeScript SDK](../sdks/typescript) — the `@dotcraft/sdk` client used inside a module.
- [Feishu Channel Adapter](../channels/feishu) — a complete module that implements this contract.
