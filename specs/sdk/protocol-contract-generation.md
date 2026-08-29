# AppServer protocol contracts and SDK generation

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Living |
| **Date** | 2026-08-03 |
| **Parent Specs** | [SDK](sdk.md), [AppServer Protocol](../protocols/appserver-protocol.md) |
| **Related Specs** | [TypeScript SDK](typescript.md), [.NET SDK](dotnet.md), [App Binding](../protocols/app-binding.md), [External Channel Adapter](../protocols/external-channel-adapter.md) |

Purpose: define the executable AppServer wire contract, typed RPC catalog, deterministic contract artifacts, and generated low-level bindings shared by the .NET and TypeScript SDKs.

## 1. Overview

AppServer is a bidirectional JSON-RPC protocol. Its public surface includes client requests, client notifications, server requests, and server notifications. Each stable method needs a single machine-readable association between its method name, direction, parameter type, result type, capability requirements, and protocol metadata.

The contract system has four layers:

```text
Markdown protocol specifications
              +
C# wire contracts and typed RPC catalog
              |
              v
        Unified Contract IR
              |
              +-- Manifest / JSON Schema / OpenRPC / contract hash
              +-- Session item payload catalog
              +-- .NET typed RPC bindings
              +-- TypeScript DTOs and method maps
```

The generated layer covers wire contracts and low-level method bindings. Transports, high-level SDK objects, run aggregation, callback orchestration, error hierarchies, and raw JSON-RPC escape hatches remain handwritten.

## 2. Goals

The contract system must:

- provide one executable C# definition for every stable AppServer wire DTO;
- bind every public AppServer method to its direction, params, result, and protocol metadata;
- bind every canonical Session item `payloadKind` to one named payload DTO;
- derive all generated artifacts from one normalized Contract IR;
- preserve required, optional, nullable, enum, union, and opaque JSON semantics across languages;
- generate deterministic artifacts without starting AppServer or loading runtime services;
- let the server and .NET SDK share the same C# contracts;
- generate typed TypeScript low-level bindings while preserving idiomatic high-level SDKs;
- retain raw request, notification, and unknown-message fallbacks;
- detect contract drift and classify protocol changes locally before artifacts are committed.

## 3. Scope

This specification defines:

- the `DotCraft.Protocol` assembly dependency boundary and its public namespaces;
- the typed RPC descriptor and catalog model;
- wire DTO rules and the supported cross-language type system;
- canonical Session item payload DTOs, parsing, and unknown-kind fallback;
- Contract IR construction and validation;
- AppServer Manifest, JSON Schema, OpenRPC, and contract hash artifacts;
- generated .NET and TypeScript wire bindings;
- typed server dispatch and notification/request emission;
- first-party AppServer contract modules;
- compatibility, deterministic generation, local validation, and protocol diff behavior.

This specification does not define:

- AppServer method semantics, which remain owned by the AppServer Protocol and its related feature specs;
- Hub HTTP contracts or Hub SDK DTO generation;
- transport framing, connection discovery, retry, or authentication behavior;
- high-level `DotCraft`, `Thread`, Run, App Binding workflow, or channel adapter APIs;
- third-party dynamic extension code generation;
- CI workflow integration or artifact publication to an external registry;
- protocol behavior changes, migrations, or a new AppServer protocol version.

## 4. Sources of truth

### 4.1 Normative protocol semantics

Markdown protocol specifications own public behavior, method semantics, field meaning, ordering guarantees, failure behavior, and compatibility policy. A behavior change updates the owning specification before the executable contract.

### 4.2 Executable contract

`DotCraft.Protocol` owns the C# wire DTOs and typed RPC descriptors used to build generated artifacts. Generated SDK code, schemas, and manifests must not be edited as independent protocol sources.

The executable contract must agree with the normative specs. Each RPC descriptor carries a valid `SpecRef` that resolves to the owning specification section. Validation fails when a reference is missing or malformed.

### 4.3 Derived artifacts

Contract artifacts and generated SDK files are reproducible outputs. They may be checked in for review and packaging, but changes to them must originate from the C# contracts, RPC catalog, or generator implementation.

## 5. Project and dependency boundaries

### 5.1 Contracts

The `DotCraft.Protocol` assembly exposes two public namespaces:

- `DotCraft.Protocol` for common contract primitives, extensibility, serialization, and RPC descriptors;
- `DotCraft.Protocol.AppServer` for AppServer DTOs, payloads, and RPC descriptors.

It contains:

- AppServer wire DTOs;
- the canonical Session item payload catalog and typed payload parser;
- wire enums and discriminated unions;
- `RpcEmpty` and `Optional<T>`;
- RPC descriptor primitives;
- the core and first-party module catalogs;
- generated catalog indexes and `JsonSerializerContext` registrations.

It may depend only on the .NET base class library and System.Text.Json. It must not reference Core, persistence, agent runtime, Desktop, Hub, database, provider implementation, or first-party module runtime assemblies.

Core, the .NET SDK, and first-party runtime modules reference Contracts. Contracts never reference those consumers.

Core must not define a structurally synonymous AppServer wire model. A Core type may remain only when it owns domain lifecycle, persistence, runtime behavior, or an internal projection that is not serialized as the AppServer contract. Hybrid types that combine wire fields with ignored runtime state must be split at the boundary.

### 5.2 Compile-time generator and analyzer

`DotCraft.Protocol.Generators` is a separate Roslyn incremental generator and analyzer. It must not be merged into the existing module/tool generator project.

It is responsible for:

- discovering typed RPC descriptor declarations;
- generating module-local and aggregate catalog indexes;
- generating serializer context registrations and C# binding helpers;
- reporting compile-time diagnostics for invalid contract declarations.

It does not write repository files and does not emit TypeScript, Manifest, Schema, or OpenRPC artifacts.

### 5.3 Repository generator

`DotCraft.ProtocolGen` is a repo-local .NET CLI. It loads the compiled contract assembly and generated catalog, builds the Contract IR, validates the complete graph, and writes deterministic repository artifacts. JSON Schema, OpenRPC, Manifest, and language bindings are derived outputs and must not be edited as protocol sources.

The CLI must not construct the AppServer host, resolve dependency injection services, inspect a workspace, read user configuration, or perform network access.

## 6. Wire contract model

### 6.1 Named DTO requirement

Stable request params, request results, notification params, server-request params, and server-request results use named wire DTOs. Anonymous objects and `object?` are not valid stable contract types.

Raw JSON remains valid for deliberately open boundaries, including:

- MCP content, resource contents, schemas, `structuredContent`, and `_meta`;
- extension-defined metadata and capability extension values;
- localized system-event parameter dictionaries;
- provider-native or otherwise opaque payloads whose unknown fields must survive round trips.

These boundaries use `JsonElement`, `JsonNode`, `JsonValue`, or a dictionary whose values are an explicit JSON value type.

### 6.2 Empty values

`RpcEmpty` represents an empty params or result object and serializes as `{}`. The contract system does not create a distinct empty type for each method.

### 6.3 Required, optional, and nullable fields

Contract properties must make presence and nullability explicit.

| C# declaration | Wire meaning | TypeScript |
|----------------|--------------|------------|
| `required T` | required, non-null | `field: T` |
| `[JsonRequired] T?` | required, nullable | `field: T \| null` |
| `T?` | optional, nullable | `field?: T \| null` |
| `Optional<T>` | optional, explicit null invalid | `field?: T` |
| `Optional<T?>` | missing, null, or value | `field?: T \| null` |

The analyzer rejects public contract properties whose declaration does not resolve to one of these states. Patch, update, and merge requests use `Optional<T>` where omission differs from an explicit value.

### 6.4 Enums and open string sets

Closed wire enums serialize as camelCase strings. TypeScript represents them as string unions.

A field that must accept future string values is modeled as an open string set in the Contract IR rather than a closed enum. Generated clients expose known constants without rejecting unknown non-empty wire values.

### 6.5 Discriminated unions

Public unions require a stable string discriminator such as `type`. Every variant must have a unique discriminator value and a named payload type. The analyzer rejects public unions without a discriminator or with overlapping discriminator values.

### 6.6 Primitive and collection rules

- Protocol identifiers are strings.
- Integer values whose protocol domain is bounded to JavaScript's safe range may use
  `[JsonSafeInteger] long`/`long?` in C#. Contract IR records the inclusive
  `-(2^53-1)` to `2^53-1` bounds, JSON Schema emits those bounds, and TypeScript
  uses `number`. Producers must not emit values outside that range.
- Values that can legitimately exceed JavaScript's safe integer range are strings on the wire.
- Timestamps use ISO 8601 UTC strings. C# may expose `DateTimeOffset`; TypeScript wire models retain their serialized string form unless a language binding explicitly wraps them at a higher layer.
- Arrays preserve order.
- Dictionaries have string keys and an explicitly modeled value type.
- Contract collections use read-only interfaces in public C# declarations where mutation is not part of the wire contract.

### 6.7 Serializer behavior

Wire names use explicit `JsonPropertyName` declarations. Optional fields use explicit null/default omission rules. Unknown fields are ignored by C# deserialization and preserved on the wire.

Custom converters are prohibited unless the contract system has an explicit schema/IR adapter for that converter. Built-in string-enum and approved discriminated-union handling are supported.

### 6.8 Session item payloads

`SessionItem.payload` remains an optional opaque JSON value so clients can retain unknown future payloads. `SessionItem.payloadKind` selects a named canonical payload DTO when the kind is known. The executable Contracts assembly owns one payload catalog; generators, parsers, and SDKs must not maintain independent kind-to-type maps.

The canonical catalog contains `userMessage`, `agentMessage`, `reasoningContent`, `commandExecution`, `toolExecution`, `imageGeneration`, `toolCall`, `mcpToolCall`, `dynamicToolCall`, `toolResult`, `approvalRequest`, `approvalResponse`, `userInputRequest`, `userInputResponse`, `error`, and `systemNotice`.

Canonical payload DTOs are extensible objects and preserve unknown fields. Parsing has these outcomes:

- a known kind with a valid payload returns the catalog DTO and the original JSON;
- a known kind with malformed JSON fails with a protocol serialization error;
- an unknown kind returns no typed value and preserves the original JSON;
- a missing payload, explicit `null`, and a JSON value remain distinguishable.

The catalog makes payload DTOs reachable roots in Contract IR even though `SessionItem.payload` is opaque JSON.

## 7. Typed RPC catalog

### 7.1 Descriptor primitives

The catalog exposes these core concepts:

```csharp
public enum RpcDirection
{
    ClientToServer,
    ServerToClient
}

public enum RpcStability
{
    Stable,
    Experimental
}

public sealed record RpcRequest<TParams, TResult>(
    string Name,
    RpcDirection Direction,
    string Since,
    string SpecRef,
    string Module = "core",
    RpcStability Stability = RpcStability.Stable);

public sealed record RpcNotification<TParams>(
    string Name,
    RpcDirection Direction,
    string Since,
    string SpecRef,
    string Module = "core",
    RpcStability Stability = RpcStability.Stable);
```

Descriptors also expose capability, scope, notification opt-out, and stable error-code metadata through typed properties rather than unstructured dictionaries.

### 7.2 Four protocol directions

The catalog covers:

| Kind | Direction | Example responsibility |
|------|-----------|------------------------|
| **Request** | Client to server | A client calls an AppServer operation and receives a typed result. |
| **Notification** | Client to server | A client sends a one-way lifecycle signal. |
| **Request** | Server to client | AppServer requests approval, user input, tool execution, or another callback. |
| **Notification** | Server to client | AppServer publishes lifecycle or state changes. |

Every public method has exactly one descriptor. Direction and request/notification kind are part of identity and cannot vary by transport.

### 7.3 Catalog declaration and discovery

Domain catalog classes expose public static descriptor fields grouped by protocol area. The Roslyn generator discovers fields whose types are `RpcRequest<,>` or `RpcNotification<>` and generates an ordered aggregate catalog.

Generation order is independent of source-file order. Methods sort by wire name, direction, and kind. Duplicate wire identities fail compilation.

### 7.4 Modules

Each descriptor has a stable module identifier. Core and bundled first-party contracts live in the pure Contracts assembly but remain separate modules in the Manifest.

The bundled module set includes core AppServer, App Binding, Automations, Teams, ACP, Node REPL, and External Channel contracts. Dynamic third-party extensions remain outside generated coverage and use the existing raw extension interface.

## 8. Contract IR

### 8.1 Model

All emitters consume one normalized in-memory model:

```text
ProtocolModel
|-- Metadata
|-- Modules
|-- Types
|   |-- Object
|   |-- Enum
|   |-- OpenStringSet
|   |-- Union
|   |-- Array
|   |-- Map
|   `-- AnyJson
|-- ItemPayloads
|   `-- payloadKind -> Type
`-- Methods
    |-- Request
    `-- Notification
```

Each field records its wire name, type reference, requiredness, nullability, description, deprecation state, and constraints. Each method records the full descriptor metadata and the resolved type identities of its params and result.

### 8.2 Construction

ProtocolGen reads generated catalog metadata and System.Text.Json serializer metadata from the compiled Contracts assembly. The TypeScript emitter does not reflect C# independently and does not parse other generated files.

### 8.3 Validation

IR construction fails on:

- duplicate method or type identities;
- missing params, result, or payload types;
- invalid or unresolved `SpecRef` values;
- Core/domain/runtime type leakage;
- ambiguous field presence or nullability;
- unsupported custom converters or primitive mappings;
- unsafe numeric mappings;
- invalid union discriminators;
- schema-name or generated-symbol collisions;
- unreachable or orphaned public contract types;
- nondeterministic ordering or output.

Validation reports stable diagnostic codes and identifies the descriptor or type that caused the failure.

## 9. Generated contract artifacts

### 9.1 Repository layout

Generated AppServer artifacts live under:

```text
src/DotCraft.Protocol/Artifacts/AppServer/
|-- appserver.manifest.json
|-- openrpc.json
|-- contract.sha256
`-- schemas/
    |-- appserver.schema.json
    `-- <module>/<Type>.schema.json
```

Artifacts are colocated with the executable contract that owns them. They remain repository outputs rather than compiled resources or independent protocol sources. They contain no timestamps, checkout paths, machine names, credentials, user identities, external project references, or environment-specific values.

### 9.2 Manifest

The Manifest is the complete machine-readable contract directory. Format version 1 contains:

- contract and AppServer protocol versions;
- modules and their stability metadata;
- type identities and schema references;
- canonical Session item payload kind-to-type entries;
- method name, C# descriptor member, kind, direction, params, and result references;
- capability, scope, notification opt-out, stable error codes, `Since`, and `SpecRef`;
- generator format version.

SDK method registries and method maps derive from the Manifest or the same IR data. JSON Schema alone is not a method catalog.

### 9.3 JSON Schema

ProtocolGen emits JSON Schema Draft 2020-12. It writes one stable file per public type and an aggregate AppServer bundle. `$id` and `$ref` values are repository-independent and use stable logical names.

Schema normalization resolves required/nullable semantics, assigns stable union variant titles, represents open string sets explicitly, and preserves descriptions needed by language generators.

### 9.4 OpenRPC

OpenRPC describes request methods and their params/results. DotCraft extensions record direction, module, capability, stability, notification semantics, and opt-out metadata where the base format is insufficient.

OpenRPC is an interoperability and documentation artifact. It is not the executable source for server dispatch.

### 9.5 Contract hash

`contract.sha256` is the SHA-256 digest of the canonical Manifest and all referenced Schema files. Inputs sort by logical path and use UTF-8 with LF line endings. Each hash entry is the logical path, one zero byte, the canonical file bytes, and one zero byte. OpenRPC is excluded because it is a redundant projection.

Generated SDKs expose the hash as protocol metadata. The hash identifies an exact contract shape; it does not replace semantic version compatibility checks.

## 10. ProtocolGen commands

The repo-local CLI provides:

```text
dotnet run --project tools/DotCraft.ProtocolGen -- generate
dotnet run --project tools/DotCraft.ProtocolGen -- validate [--module <name>]...
dotnet run --project tools/DotCraft.ProtocolGen -- check
dotnet run --project tools/DotCraft.ProtocolGen -- diff --against <baseline>
```

Every command accepts `--profile stable|experimental`; `stable` is the default. Repeated `--module` options limit `validate` to selected bundled modules without changing type identities.

- `generate` writes all selected artifacts and generated SDK files.
- `validate` builds and validates the Contracts/IR graph without invoking language code generators or changing tracked files.
- `check` generates into an isolated temporary directory and reports drift from checked-in outputs.
- `diff` classifies changes between two contract packages.

Generation first compiles and validates the complete Contract IR, then renders every contract and SDK output into an isolated staging directory. Files are normalized before hashing. Installation replaces each destination through a same-directory temporary file only after the complete staged output succeeds; generation failures before installation leave checked-in artifacts unchanged. `check` performs the same construction and validation without writing repository files.

These commands run locally. Generated artifacts are reviewed and committed manually. CI workflow integration requires a separate approved change.

## 11. Language binding generation

### 11.1 .NET

The server and .NET SDK reference `DotCraft.Protocol`. No second generated copy of the DTOs exists.

The compile-time generator emits typed low-level helpers such as:

```csharp
Task<ThreadStartResult> ThreadStartAsync(
    ThreadStartParams parameters,
    CancellationToken cancellationToken = default);
```

The .NET SDK exposes descriptor-typed `RequestAsync` / `NotifyAsync` APIs. Unknown extensions use separately named `RequestRawAsync` / `NotifyRawAsync` methods; there is no arbitrary-string overload on the typed methods.

Each Manifest method records the public `AppServerRpc` descriptor member used by generated .NET bindings. This is additive metadata in Manifest format version 1; it avoids guessing descriptor identifiers from Wire method spelling without changing Contract IR's format version.

The same generator emits notification classification for the high-level Run layer. Every cataloged server notification is deserialized to its Contracts params DTO and exposed as `DotCraftRunEvent<TParams>`. Unknown methods become `DotCraftRawRunEvent`. A known method whose params do not match its DTO raises the stable `ProtocolViolationException` instead of falling back to raw JSON.

### 11.2 TypeScript

ProtocolGen writes:

```text
sdk/typescript/src/generated/appserver/
|-- models.generated.ts
|-- client-requests.generated.ts
|-- client-notifications.generated.ts
|-- server-requests.generated.ts
|-- server-notifications.generated.ts
|-- item-payloads.generated.ts
`-- method-groups.generated.ts
```

The TypeScript emitter consumes Contract IR directly. It generates interfaces, string unions, discriminated unions, JSON value aliases, the Session item payload map and known/unknown classifier, four direction-specific method maps, and exhaustive known notification/server-request envelope unions suitable for typed dispatch and host IPC boundaries.

```typescript
export interface ClientRequestMap {
  "thread/start": {
    params: ThreadStartParams;
    result: ThreadStartResult;
  };
}
```

The low-level client uses method literals to infer params and result types. Unknown methods remain available only through explicitly named raw APIs. `@dotcraft/sdk/contracts` re-exports these I/O-free generated artifacts.

### 11.3 Handwritten SDK layer

All language bindings keep these components handwritten:

- transports and response correlation;
- initialization gating, timeouts, lifecycle state, and opt-in reconnect;
- Hub discovery and connection management;
- high-level `DotCraft`, `Thread`, and Run APIs;
- event reduction and text merging;
- callback orchestration;
- language-specific exception types;
- explicitly named raw escape hatches.

Generated files remain internal low-level building blocks unless a language binding spec explicitly exports them.

Known operations use generated maps, descriptors, or mixins. Unknown extensions use separately named raw methods so a misspelled known method cannot silently bypass compile-time checking. Host adapters may project generated contracts across IPC, but must not fork the contract model.

The .NET high-level layer directly exposes Contracts DTOs for initialize, Thread, Turn, provider/model, MCP, App Binding, approval, user input, Runtime Dynamic Tool callbacks, snapshots, and terminal Run state. High-level handles, reducers, Hub helpers, authoring attributes, and exceptions remain SDK-owned because they are not Wire DTOs.

## 12. Typed server integration

The server maps typed descriptors rather than unrelated strings and `object?` handlers:

```csharp
public void Map<TParams, TResult>(
    RpcRequest<TParams, TResult> method,
    RpcHandler<TParams, TResult> handler);
```

Notification and reverse-request writers accept the same descriptor identities:

```csharp
await writer.NotifyAsync(ThreadRpc.Started, notification, cancellationToken);
await connection.RequestAsync(ApprovalRpc.Request, parameters, cancellationToken);
```

Typed dispatch owns parameter deserialization, validation, handler invocation, result serialization, cancellation propagation, and stable error mapping. Handlers receive Contracts parameter DTOs and return Contracts result DTOs. Domain mapping remains explicit:

```text
Contract DTO -> Domain command/input
Domain model -> Contract DTO
```

Contract DTOs must not become persistence or domain models.

Generic JSON serialization round trips are not a substitute for domain mapping. Feature-owned mappers must read or construct the relevant fields explicitly so contract drift is visible to compilation and tests.

The generic JSON-RPC envelope, transport interfaces, and raw extension interface remain available. Typed dispatch preserves response-before-notification ordering and notification filtering behavior.

## 13. First-party modules and extensions

Bundled first-party methods participate in the aggregate Catalog through stable module identifiers. Their DTOs remain in the pure Contracts assembly even when runtime implementations live in separate assemblies.

The aggregate generator includes core and bundled modules in one contract package. Module filtering may produce a subset for validation or development, but the default package represents the complete bundled AppServer surface.

Third-party extensions that do not ship a contract module continue to declare string method names and accept raw JSON. Dynamic third-party contract discovery and SDK generation are future work.

Hub endpoints are not AppServer modules. They do not enter the RPC Catalog, Manifest, or AppServer OpenRPC document.

## 14. Protocol evolution

### 14.1 Wire invariants

Contract and binding changes must preserve:

- JSON property names and omission rules;
- requiredness and nullability;
- enum and discriminator values;
- array and notification ordering;
- response-before-notification behavior;
- cancellation and timeout behavior;
- error codes and error-data shapes;
- unknown-field and opaque JSON behavior.

### 14.2 Executable authority

The `DotCraft.Protocol` assembly is the sole executable authority for public AppServer DTOs and method associations. Runtime assemblies may retain domain-facing projection types, and SDKs may retain high-level models, but neither is an independent wire definition. Core request/result/notification DTOs and handwritten SDK wire DTOs are prohibited. Built-in registrations, notifications, fixed reverse requests, and canonical method-name constants are generated from typed descriptors. Handwritten method-name facades are not permitted.

### 14.3 Raw extension boundary

Every SDK exposes explicitly named raw request and notification APIs. Unknown notifications are exposed through a distinct raw listener. The server accepts unmodeled third-party extensions only through the raw extension interface; bundled extensions must register every method through a typed descriptor.

### 14.4 Diff classification

Local protocol diff classifies changes as:

| Classification | Examples |
|----------------|----------|
| **Breaking** | Method removal or rename, direction/kind change, field removal, required field addition, optional-to-required, nullable-to-non-null, type narrowing, discriminator change, closed-enum value removal, payload kind removal, or reassignment of an existing payload kind to another DTO. |
| **Additive** | Optional field addition, new method, new payload kind, new union variant where unknown variants are supported, new open-string known value. |
| **Metadata-only** | Description or `SpecRef` correction that does not change wire shape. |

Closed-enum additions are reported separately as source-compatibility risks even when wire-compatible.

## 15. Stability profiles

Every descriptor and public type carries stability metadata. All current bundled descriptors are stable unless explicitly declared otherwise. Local generation supports two profiles:

- stable output, containing stable contract entries only;
- experimental output, containing stable and experimental entries.

Experimental filtering happens in Contract IR before any emitter runs. Emitters must not remove experimental entries by rewriting generated text.

## 16. Testing and validation

### 16.1 C# contracts

Tests cover exact serialization, required/optional/null semantics, Session Wire field-declaration parity, all canonical item payloads, unknown payload fallback, enums, unions, opaque JSON, unknown fields, descriptor uniqueness, analyzer diagnostics, and typed dispatch.

### 16.2 Artifacts

Tests generate artifacts twice and compare bytes. They validate ordering, references, aggregate schemas, Manifest/OpenRPC agreement, stable names, and contract hash reproduction.

### 16.3 Cross-language fixtures

Shared JSON fixtures cover initialization, thread start/resume/list/read, turn start and terminal states, approval, user input, dynamic tools, lifecycle notifications, errors, empty objects, unknown fields, and extension payloads.

The same fixtures are consumed by xUnit and the TypeScript test runner.

The portable message fixtures under `specs/protocols/fixtures/` are durable protocol assets. Fixtures use synthetic identifiers and values. They must not contain machine-specific paths, credentials, user identities, or references to external projects.

### 16.4 TypeScript

Tests compile generated method maps and validate DTO round trips, discriminator narrowing, optional/null behavior, raw unknown notifications, and generated client method inference.

### 16.5 Integration invariants

Existing AppServer integration suites continue to verify cancellation, response ordering, notification filtering, server-initiated callbacks, raw escape hatches, and high-level SDK behavior.

## 17. Local artifact workflow

Contract changes follow this order:

1. Update the owning Markdown protocol spec.
2. Update C# contract DTOs and RPC descriptors.
3. Build Contracts and run analyzer validation.
4. Run ProtocolGen `generate`.
5. Run ProtocolGen `check` and the shared conformance tests.
6. Review and commit the source and generated artifact changes together.

CI workflows do not enforce these commands. Automated drift and compatibility gates require separate approval.

## 18. Implementation state

The repository implements the complete bundled AppServer contract surface across core, App Binding, Automations, Teams, ACP, Node REPL, and External Channel modules. The checked-in Manifest is the authoritative machine-readable method inventory; generated schemas, OpenRPC, and TypeScript bindings are projections of the same IR.

Server dispatch uses typed descriptors for bundled methods while preserving the generic JSON-RPC envelope and raw third-party extension path. Portable fixtures remain durable conformance assets. CI enforcement, Hub OpenAPI generation, external artifact publication, and dynamic third-party contract generation remain outside this specification.

## 19. Acceptance checklist

- [x] Markdown specs remain the normative behavior contract.
- [x] Server and .NET SDK share one C# wire contract assembly.
- [x] Every public AppServer method has one typed descriptor.
- [x] All four JSON-RPC directions are modeled.
- [x] All emitters derive from one Contract IR.
- [x] Manifest, Schema, OpenRPC, and contract hash are deterministic.
- [x] TypeScript bindings provide typed method maps and raw fallbacks.
- [x] Missing, null, enum, union, and opaque JSON semantics agree across languages.
- [x] Session Thread, Turn, and Item DTOs explicitly declare the complete public Wire shape.
- [x] All canonical Session item payloads share one generated cross-language catalog and unknown fallback.
- [x] Typed server dispatch preserves existing wire behavior.
- [x] Core and bundled first-party modules have generated coverage.
- [x] Hub and third-party dynamic extensions remain outside this contract package.
- [x] Local generation and diff commands work without runtime services or network access.
- [x] Generated artifacts are manually reviewed and committed with source changes.
- [x] CI workflow integration remains outside this specification.

## 20. Open questions

None. Changes to the source-of-truth model, generation path, Hub boundary, artifact policy, or stability profile policy require an amendment to this specification.

## Related docs

- [SDK](sdk.md)
- [AppServer Protocol](../protocols/appserver-protocol.md)
- [TypeScript SDK](typescript.md)
- [.NET SDK](dotnet.md)
