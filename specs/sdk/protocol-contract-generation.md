# AppServer protocol contracts and SDK generation

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Living |
| **Date** | 2026-10-19 |
| **Parent Specs** | [AppServer Protocol](../protocols/appserver-protocol.md) |

Purpose: define the executable AppServer wire contract, typed RPC catalog, deterministic contract artifacts, and generated low-level bindings shared by the .NET and TypeScript SDKs.

## 1. Overview

AppServer is a bidirectional JSON-RPC protocol. Its public surface includes client requests, client notifications, server requests, and server notifications. Each stable method needs a single machine-readable association between its method name, direction, parameter type, result type, capability requirements, and protocol metadata.

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

## 2. Scope

This specification owns the executable contract boundary, cross-language type rules, RPC catalog, deterministic artifacts, and generation workflow. AppServer and its protocol modules own behavior. Hub HTTP, high-level SDK orchestration, dynamic third-party generation, and external artifact publication are outside this contract package.

## 3. Sources of truth

### 3.1 Normative protocol semantics

Markdown protocol specifications own public behavior, method semantics, field meaning, ordering guarantees, failure behavior, and compatibility policy. A behavior change updates the owning specification before the executable contract.

### 3.2 Executable contract

`DotCraft.Protocol` is the sole executable authority for public AppServer DTOs and method associations. Generated SDK code, schemas, and manifests must not be edited as independent protocol sources. Runtime assemblies may retain domain-facing projection types and SDKs may retain high-level models, but neither is an independent wire definition: Core request/result/notification DTOs, handwritten SDK wire DTOs, and handwritten method-name facades are prohibited. Built-in registrations, notifications, fixed reverse requests, and canonical method-name constants derive from typed descriptors.

The executable contract must agree with the normative specs. Each RPC descriptor carries a valid `SpecRef` that resolves to the owning specification section; validation fails when a reference is missing or malformed.

### 3.3 Derived artifacts

Contract artifacts and generated SDK files are reproducible outputs. They may be checked in for review and packaging, but changes to them must originate from the C# contracts, RPC catalog, or generator.

## 4. Dependency boundaries

`DotCraft.Protocol` exposes the `DotCraft.Protocol` namespace (contract primitives, extensibility, serialization, RPC descriptors) and the `DotCraft.Protocol.AppServer` namespace (AppServer DTOs, payloads, and descriptors). It contains the wire DTOs, wire enums and unions, `RpcEmpty`, `Optional<T>`, the Session item payload catalog, and the core and first-party module catalogs.

It may depend only on the .NET base class library and System.Text.Json. Core, the .NET SDK, and first-party runtime modules reference it; it never references them or any persistence, agent runtime, Desktop, Hub, database, or provider assembly.

Core must not define a structurally synonymous AppServer wire model. A Core type may remain only when it owns domain lifecycle, persistence, runtime behavior, or an internal projection that is not serialized as the AppServer contract. Hybrid types that combine wire fields with runtime state are split at the boundary.

A compile-time generator and analyzer discovers descriptors, generates catalog indexes, serializer registrations, and C# binding helpers, and reports invalid contract declarations; it does not write repository files. The repo-local `DotCraft.ProtocolGen` CLI loads the compiled contract assembly, builds and validates the Contract IR, and writes the repository artifacts. It must not construct the AppServer host, resolve services, inspect a workspace, read user configuration, or access the network.

## 5. Wire contract model

### 5.1 Named DTO requirement

Stable request params, request results, notification params, server-request params, and server-request results use named wire DTOs. Anonymous objects and `object?` are not valid stable contract types.

Raw JSON remains valid for deliberately open boundaries, including:

- MCP content, resource contents, schemas, `structuredContent`, and `_meta`;
- extension-defined metadata and capability extension values;
- localized system-event parameter dictionaries;
- provider-native or otherwise opaque payloads whose unknown fields must survive round trips.

These boundaries use an explicit JSON value type.

### 5.2 Empty values

`RpcEmpty` represents an empty params or result object and serializes as `{}`. There is no distinct empty type per method.

### 5.3 Required, optional, and nullable fields

Contract properties must make presence and nullability explicit.

| C# declaration | Wire meaning | TypeScript |
|----------------|--------------|------------|
| `required T` | required, non-null | `field: T` |
| `[JsonRequired] T?` | required, nullable | `field: T \| null` |
| `T?` | optional, nullable | `field?: T \| null` |
| `Optional<T>` | optional, explicit null invalid | `field?: T` |
| `Optional<T?>` | missing, null, or value | `field?: T \| null` |

The analyzer rejects public contract properties that do not resolve to one of these states. Patch, update, and merge requests use `Optional<T>` where omission differs from an explicit value.

### 5.4 Enums and open string sets

Closed wire enums serialize as camelCase strings and are string unions in TypeScript.

A field that must accept future string values is an open string set rather than a closed enum. Generated clients expose known constants without rejecting unknown non-empty wire values.

### 5.5 Discriminated unions

Public unions require a stable string discriminator such as `type`. Every variant has a unique discriminator value and a named payload type.

### 5.6 Primitive and collection rules

- Protocol identifiers are strings.
- Integers whose domain is bounded to JavaScript's safe range may use `[JsonSafeInteger] long`; the contract records the `-(2^53-1)` to `2^53-1` bounds, JSON Schema emits them, and TypeScript uses `number`. Producers must not emit values outside that range.
- Values that can exceed JavaScript's safe integer range are strings on the wire.
- Timestamps are ISO 8601 UTC strings. TypeScript wire models keep the string form.
- Arrays preserve order.
- Dictionaries have string keys and an explicitly modeled value type.

### 5.7 Serializer behavior

Wire names use explicit `JsonPropertyName` declarations. Optional fields use explicit omission rules. Unknown fields are ignored by C# deserialization and preserved on the wire.

Custom converters are prohibited unless the contract system has an explicit schema/IR adapter for them. Built-in string-enum and approved discriminated-union handling are supported.

### 5.8 Session item payloads

`SessionItem.payload` remains an optional opaque JSON value so clients can retain unknown future payloads. `SessionItem.payloadKind` selects a named canonical payload DTO when the kind is known. The Contracts assembly owns the one payload catalog; generators, parsers, and SDKs must not maintain independent kind-to-type maps.

The canonical catalog contains `userMessage`, `agentMessage`, `reasoningContent`, `commandExecution`, `toolExecution`, `imageGeneration`, `webSearch`, `toolCall`, `mcpToolCall`, `dynamicToolCall`, `toolResult`, `approvalRequest`, `approvalResponse`, `userInputRequest`, `userInputResponse`, `error`, and `systemNotice`.

Canonical payload DTOs are extensible objects and preserve unknown fields. Parsing has these outcomes:

- a known kind with a valid payload returns the catalog DTO and the original JSON;
- a known kind with malformed JSON fails with a protocol serialization error;
- an unknown kind returns no typed value and preserves the original JSON;
- a missing payload, explicit `null`, and a JSON value remain distinguishable.

The catalog makes payload DTOs reachable contract roots even though `SessionItem.payload` is opaque JSON.

## 6. Typed RPC catalog

### 6.1 Descriptors

Each public method has exactly one typed descriptor (`RpcRequest<TParams, TResult>` or `RpcNotification<TParams>`) carrying its wire name, direction, `Since`, `SpecRef`, module, stability, and typed capability, scope, notification opt-out, and stable error-code metadata.

The catalog covers four kinds: client-to-server requests and notifications, and server-to-client requests (approval, user input, tool execution, other callbacks) and notifications (lifecycle and state changes). Direction and request/notification kind are part of method identity and cannot vary by transport.

Catalog order is independent of source order: methods sort by wire name, direction, and kind. Duplicate wire identities fail compilation. Catalog indexes and method-name constants are generated only in the compilation that declares the RPC descriptors, not in SDK or runtime consumers that reference the compiled contract assembly.

### 6.2 Modules

Each descriptor has a stable module identifier. Core and bundled first-party contracts (core AppServer, App Binding, Automations, ACP, Node REPL, External Channel) live in the Contracts assembly but remain separate modules in the Manifest, even when their runtime implementations live in separate assemblies. The default package represents the complete bundled AppServer surface; module filtering may produce a subset for validation.

Third-party extensions that do not ship a contract module declare string method names and accept raw JSON; dynamic third-party contract discovery is outside this package. Hub endpoints are not AppServer modules and do not enter the catalog, Manifest, or OpenRPC.

## 7. Contract IR

All emitters consume one normalized model of metadata, modules, types (object, enum, open string set, union, array, map, any-JSON), item payloads (`payloadKind` → type), and methods. Each field records its wire name, type, requiredness, nullability, description, deprecation, and constraints. Each method records its full descriptor metadata and resolved params and result types.

The IR is built from catalog metadata and System.Text.Json serializer metadata of the compiled Contracts assembly. Language emitters consume the IR; they do not reflect C# independently or parse other generated files.

IR construction fails on:

- duplicate method or type identities;
- missing params, result, or payload types;
- invalid or unresolved `SpecRef` values;
- Core/domain/runtime type leakage;
- ambiguous field presence or nullability;
- unsupported custom converters or primitive mappings;
- unsafe numeric mappings;
- missing or overlapping union discriminators;
- schema-name or generated-symbol collisions;
- unreachable or orphaned public contract types;
- nondeterministic ordering or output.

Validation reports stable diagnostic codes identifying the offending descriptor or type.

## 8. Generated contract artifacts

### 8.1 Artifact set

The AppServer contract package consists of the Manifest (`appserver.manifest.json`), OpenRPC (`openrpc.json`), the contract hash (`contract.sha256`), and JSON Schemas (an aggregate bundle plus one file per type, grouped by module). Artifacts are colocated with the executable contract and contain no timestamps, checkout paths, machine names, credentials, user identities, external project references, or environment-specific values.

### 8.2 Manifest

The Manifest is the complete machine-readable contract directory. Format version 1 contains:

- contract and AppServer protocol versions;
- modules and their stability metadata;
- type identities and schema references;
- canonical Session item payload kind-to-type entries;
- method name, C# descriptor member, kind, direction, params, and result references;
- capability, scope, notification opt-out, stable error codes, `Since`, and `SpecRef`;
- generator format version.

SDK method registries and method maps derive from the Manifest or the same IR data. JSON Schema alone is not a method catalog.

### 8.3 JSON Schema

Schemas use JSON Schema Draft 2020-12. `$id` and `$ref` values are repository-independent stable logical names. Schemas resolve required/nullable semantics, assign stable union variant titles, represent open string sets explicitly, and preserve descriptions needed by language generators.

### 8.4 OpenRPC

OpenRPC describes request methods and their params/results, with DotCraft extensions for direction, module, capability, stability, notification semantics, and opt-out metadata. It is an interoperability and documentation artifact, not the executable source for server dispatch.

### 8.5 Contract hash

`contract.sha256` is the SHA-256 digest of the canonical Manifest and all referenced Schema files. Inputs sort by logical path and use UTF-8 with LF line endings. Each hash entry is the logical path, one zero byte, the canonical file bytes, and one zero byte. OpenRPC is excluded as a redundant projection.

Generated SDKs expose the hash as protocol metadata. It identifies an exact contract shape; it does not replace semantic version compatibility checks.

## 9. ProtocolGen commands

```text
dotnet run --project tools/DotCraft.ProtocolGen -- generate
dotnet run --project tools/DotCraft.ProtocolGen -- validate [--module <name>]...
dotnet run --project tools/DotCraft.ProtocolGen -- check
dotnet run --project tools/DotCraft.ProtocolGen -- diff --against <baseline>
```

Every command accepts `--profile stable|experimental` (default `stable`). `--module` limits `validate` to selected bundled modules without changing type identities.

- `generate` writes all contract artifacts and generated SDK files. A failure leaves checked-in artifacts unchanged.
- `validate` builds and validates the Contracts/IR graph without running language emitters or changing tracked files.
- `check` regenerates in isolation and reports drift from checked-in outputs.
- `diff` classifies changes between two contract packages (§12.4).

## 10. Language bindings

### 10.1 Generated surface

The server and .NET SDK reference `DotCraft.Protocol` directly; no second copy of the DTOs exists. Generated .NET helpers are typed per method and call the exact descriptor member recorded in the Manifest. Generated server-notification classification distinguishes unknown methods from malformed known messages.

TypeScript DTOs are emitted from the IR: interfaces, string unions, discriminated unions, JSON value aliases, the Session item payload map with a known/unknown classifier, four direction-specific method maps, and exhaustive known notification and server-request envelope unions for typed dispatch and host IPC. Method literals infer params and result types.

### 10.2 Handwritten layer

Every language binding keeps these handwritten:

- transports and response correlation;
- initialization gating, timeouts, lifecycle state, and opt-in reconnect;
- Hub discovery and connection management;
- high-level `DotCraft`, `Thread`, and Run APIs;
- event reduction and text merging;
- callback orchestration;
- language-specific exception types;
- explicitly named raw escape hatches.

Known operations use generated maps, descriptors, or helpers. Unknown extensions use separately named raw methods so a misspelled known method cannot bypass compile-time checking; typed methods have no arbitrary-string overload. Generated files are internal building blocks unless a language binding spec exports them. Host adapters may project generated contracts across IPC but must not fork the contract model.

## 11. Typed server integration

The server maps handlers, notification writers, and reverse requests by typed descriptor, never by unrelated strings and `object?` handlers. Typed dispatch owns parameter deserialization, validation, handler invocation, result serialization, cancellation propagation, and stable error mapping, and preserves response-before-notification ordering and notification filtering. Handlers receive Contracts params and return Contracts results.

Domain mapping between contract DTOs and domain commands/models is explicit. Contract DTOs must not become persistence or domain models, and generic JSON round trips are not a substitute for mapping: feature-owned mappers read or construct fields explicitly so contract drift is visible to compilation and tests.

The generic JSON-RPC envelope, transport interfaces, and raw extension interface remain available. The server accepts unmodeled third-party extensions only through the raw extension interface; bundled extensions register every method through a typed descriptor. Every SDK exposes explicitly named raw request and notification APIs and a distinct raw listener for unknown notifications.

## 12. Protocol evolution

### 12.1 Wire invariants

Contract and binding changes must preserve:

- JSON property names and omission rules;
- requiredness and nullability;
- enum and discriminator values;
- array and notification ordering;
- response-before-notification behavior;
- cancellation and timeout behavior;
- error codes and error-data shapes;
- unknown-field and opaque JSON behavior.

### 12.2 Stability profiles

Every descriptor and public type carries stability metadata; bundled descriptors are stable unless declared otherwise. The stable profile contains stable entries only; the experimental profile contains stable and experimental entries. Filtering happens in the IR before any emitter runs.

### 12.3 Change order

A contract change updates the owning protocol spec first, then the C# contracts and descriptors, then regenerates and checks artifacts. Source and generated artifact changes are reviewed and committed together.

### 12.4 Diff classification

| Classification | Examples |
|----------------|----------|
| **Breaking** | Method removal or rename, direction/kind change, field removal, required field addition, optional-to-required, nullable-to-non-null, type narrowing, discriminator change, closed-enum value removal, payload kind removal, or reassignment of an existing payload kind to another DTO. |
| **Additive** | Optional field addition, new method, new payload kind, new union variant where unknown variants are supported, new open-string known value. |
| **Metadata-only** | Description or `SpecRef` correction that does not change wire shape. |

Closed-enum additions are reported separately as source-compatibility risks even when wire-compatible.

## 13. Conformance assets

Portable JSON fixtures under `specs/protocols/fixtures/` are shared by .NET and TypeScript. They use synthetic values and exercise the wire contract rather than repository-specific environments. Generated artifacts must reproduce byte-for-byte, with resolvable schema references and matching Manifest/OpenRPC method identities.
