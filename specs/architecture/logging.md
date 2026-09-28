# DotCraft Logging Architecture

| Field | Value |
| --- | --- |
| Version | 0.7.8 |
| Status | Living |
| Date | 2026-09-28 |

## Overview

DotCraft uses `Microsoft.Extensions.Logging.ILogger` as the single operational
diagnostics API. Host composition selects providers and destinations without
exposing provider-specific APIs to Core services.

## Non-goals

- A DotCraft-specific logger facade over `ILogger`.
- A query service, SQLite diagnostics database, or remote telemetry exporter.
- Persisting every line written to stdout or stderr.
- Merging ACP wire logs or session stream debug records into operational logs.
- Changing CLI presentation as part of logging-provider replacement.

## Core design and architecture

Application and Core code emit operational diagnostics through injected
`ILogger<T>` instances. DotCraft does not expose or consume provider-specific
logging APIs outside the application composition root.

The application owns one `ILoggerFactory` for its lifetime. The factory fans
events out to the configured rolling file provider and, when enabled, a console
provider. Nested hosts such as ASP.NET Core forward events to that factory
without taking ownership of it. ASP.NET Core and generic host lifecycle
categories retain `Warning` and higher events but suppress routine connection,
endpoint, and host-lifetime `Information` events. DotCraft-owned lifecycle
events remain available at `Information`.

Long-lived hosts add a stable `Module` scope. AppServer connections add
`ConnectionId` and `Transport`; request handling adds `RequestMethod` and
`RequestId`. Session execution adds `ThreadId`, `TurnId`, and `Channel`.
Integration-specific events add stable channel, workspace, process, and service
identifiers without including prompts, tool results, protocol frames, or access
credentials.

Persistent logs are scoped by host ownership:

- Workspace-bound hosts write under `<workspace>/.craft/logs`.
- Hub writes under the current user's `~/.craft/logs` directory.
- ACP wire logs and session stream debug logs retain their independent opt-in
  files and retention semantics.

The default persistent format is human-readable UTF-8 text. Each entry includes
timestamp, severity, process ID, category, message, exception, and any active
diagnostic scope. Workspace logs use `dotcraft-yyyy-MM-dd_NNN.log`; Hub logs use
`dotcraft-hub-yyyy-MM-dd_NNN.log`. The sequence starts at `_000` and advances
when a file reaches the size limit. The workspace and Hub single-owner locks
prevent long-lived concurrent writers to the same host log set. A bounded
background queue applies backpressure instead of growing memory without limit.
Existing `Logging.Enabled`, `Console`, `MinLevel`, `Directory`, and
`RetentionDays` configuration remains authoritative.

## Behavioral contracts and lifecycle

The logging factory is established before a long-lived host starts and is
disposed after the host stops. Disposal flushes accepted events before process
exit. A configuration-load failure uses default logging settings long enough to
persist the fatal exception when a valid host log root can be resolved.

Logging failure is non-fatal to application work. Provider-internal failures
may report a bounded fallback message directly to stderr, but must not recurse
through `ILogger` or corrupt stdout reserved for a wire protocol.

Unhandled host and request failures are recorded with the original exception.
Expected user-facing validation errors may be rendered without a stack trace,
but operational diagnostics retain structured identifiers and exception data.

Console output has three distinct roles:

- Wire stdout is reserved exclusively for the active protocol.
- Spectre.Console renders interactive CLI presentation at the App boundary.
- Diagnostic console output is an optional logging destination and uses stderr
  whenever stdout is reserved.

Core has no terminal package dependency and never writes directly to
`Console`. Interactive approvals use the host-neutral
`IInteractiveApprovalPrompt` contract; DotCraft.App provides the Spectre.Console
implementation. Other presentation-only progress uses existing responder or
status callback surfaces.

## Constraints and compatibility

- `ILogger<T>` remains the public diagnostics abstraction.
- Existing logging configuration files continue to load without migration.
- High-volume or sensitive payloads such as prompts, model output, tool output,
  tokens, and raw protocol frames are not ordinary operational log content.
- Provider-specific extension methods are not used in business or Core code.
- Core does not depend on terminal rendering packages.
