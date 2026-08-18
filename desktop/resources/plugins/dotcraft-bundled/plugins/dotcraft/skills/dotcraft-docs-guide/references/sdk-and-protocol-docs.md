# SDK and protocol documentation

Use this reference for DotCraft SDK, AppServer, Hub, JSON-RPC, generated-contract, Desktop-client, and Channel-client documentation.

## Information architecture

Keep each page at one abstraction level:

| Page | Primary job | Include | Hand off |
|------|-------------|---------|----------|
| SDK index | Help readers choose a layer and language | Layer selection, availability, capability summary | Installation and first run to Quickstart |
| Quickstart | Reach the first successful high-level Run | The one supported install path, connect, Thread, Run, streaming | Options and wire details to references |
| Language reference | Describe the public library surface | Entry points, typed/raw APIs, lifecycle, errors, Hub capability, validation | Task flows to behavior guides |
| Behavior guide | Solve one application task | Threads/Runs, tools/approvals, or Channel policy | Exact payloads to protocol pages |
| Lifecycle page | Explain process ownership and connection boundaries | AppServer/Hub roles, startup, transport choice | Client API to SDK pages; raw messages to protocol |
| Protocol page | Define the low-level wire contract | JSON-RPC or HTTP/SSE messages, ordering, errors, compatibility | Normal application use to SDK Quickstart |

For supported languages, recommend the SDK first. Recommend direct protocol implementation only for a custom transport, an unsupported language, a dynamic extension outside the contract catalog, or protocol debugging.

## SDK layers

Use these boundaries consistently:

| Layer | Responsibility |
|-------|----------------|
| Contracts | Generated DTOs, method maps, registries, unions, and protocol metadata; no transport or host I/O. |
| Wire | JSON-RPC correlation, typed known methods, explicit raw escape hatches, initialization, lifecycle, timeout, and optional reconnect. |
| High-level | `DotCraft`, Thread, Run, approval, user input, Runtime Dynamic Tools, and application callbacks. |
| Host Adapter | Desktop or Channel policy such as workspace routing, SSH, heartbeat, platform delivery, UI interaction, and reconnect profile. |

Do not describe host policy as a general Wire guarantee. Reconnect may restore transport and initialization while leaving Thread subscriptions, active Runs, and Runtime Dynamic Tool resources for the application to recover.

## Typed and raw boundaries

- Show typed calls for cataloged methods and generated DTOs.
- Show explicitly named raw calls only for unknown or third-party extensions.
- Never demonstrate passing an arbitrary method string through a typed API.
- Keep open, polymorphic JSON raw only where the protocol intentionally permits it.
- State handler/capability invariants. Do not claim that a missing approval or user-input handler silently invents a response unless the owning host profile actually defines that policy.

## Installation and release state

Treat installation claims as time-sensitive:

1. Inspect package manifests, including `private`, package name, runtime baseline, and exported entry points.
2. Verify the public registry before claiming that npm, NuGet, or PyPI installation works.
3. If a package is source-only, say so directly and provide a tested local build or editable-install path.
4. Keep the complete installation procedure in Quickstart. Other pages should state availability and link there.
5. Distinguish current repository capabilities from the latest published package when releases lag source.

Do not infer publication from a package directory or package name. Remove commands that cannot succeed for a reader today.

## Evidence and validation

Before editing, confirm every documented symbol and behavior in its owner:

- Contracts and method direction: generated method maps, registries, descriptors, schemas, or DTOs.
- Defaults and lifecycle: client implementation and tests.
- Entry points and dependencies: package manifests and project files.
- Host behavior: Desktop or Channel adapter implementation, not the Wire client.
- Wire behavior: protocol spec plus server/client tests.

After editing:

1. Run `npm run build` in `docs/` to validate VitePress rendering and links.
2. When TypeScript APIs changed in prose or examples, run the SDK build and typecheck.
3. When Python APIs changed, run the repository's pinned static type checker; run model tests when aliases, nullability, or serialization are involved.
4. When .NET APIs changed, build the SDK solution or owning project.
5. Search all documentation for removed package names, impossible install commands, superseded option names, arbitrary-string typed calls, and obsolete fallback behavior.
6. Compare each locale pair's heading levels and code-fence sequence. Also inspect links, admonitions, and images manually.

Keep validation proportional to the documentation claim. Copy-only edits need the docs build; public API, installation, or lifecycle claims warrant the owning SDK checks.
