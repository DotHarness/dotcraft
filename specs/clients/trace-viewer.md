# DotCraft Trace Viewer

| Field | Value |
|---|---|
| Version | 0.10 |
| Status | Draft |
| Date | 2026-08-20 |
| Parent spec | `specs/sdk/harness.md` |

## Overview

DotCraft Trace Viewer is a Windows sample that demonstrates two complementary in-process .NET integration patterns: reading an existing DotCraft Trace without modifying its workspace, and embedding a dedicated DotCraft Harness Agent that reviews that Trace. The target workspace remains a read-only evidence source. The Trace Analyst runs in DotCraft Trace Viewer-owned application data and never becomes the Agent that produced the inspected Session.

The sample lives under `src/DotCraft.TraceViewer/`. Its project, assembly, and executable use `DotCraft.TraceViewer`, while the product-facing name remains DotCraft Trace Viewer.

## Goal

Provide a focused WinUI application that first presents an existing DotCraft Session as a chronological, turn-aware trajectory, then lets a developer explicitly ask a dedicated Trace Analyst to produce a structured, evidence-linked Review and answer follow-up questions. The experience must make recorded facts, Agent interpretations, and unsupported conclusions distinguishable without running a web server or modifying the target workspace.

## Scope

- An unpackaged WinUI 3 application targeting `.NET 10` and Windows x64.
- A workspace picker that discovers the workspace's direct-child data directory. It prefers `.craft` and otherwise accepts one unambiguous direct child containing `state.db`, including `.agents` and other valid `DataPath` values.
- Read-only access to the existing workspace `state.db` through DotCraft Core tracing APIs.
- Session discovery, refresh, summary information, and parent/child session relationships.
- A paged trajectory containing turn boundaries, request steps, responses, tools, diagnostics, and terminal events.
- A navigable timing overview, event search and filtering, and an on-demand event inspector.
- A Session-level `Timeline / Review` mode switch. It uses the shared neutral segmented-control treatment in every theme. Selection must not inherit the operating-system accent color.
- Explicit Agent analysis through an isolated in-process `DotCraft.Harness`.
- Structured Review summaries and Major/Minor/Suggestion Findings with evidence references.
- Continuous follow-up conversation grounded in the same immutable Trace snapshot.
- DotCraft user-data persistence for the latest successful Review and its Analyst conversation.
- A self-contained unpackaged publish output that can be run directly on Windows x64.

## Non-goals

- Running or continuing the Agent Session being inspected.
- Automatically analyzing a Session when it is opened or selected.
- Managing providers, credentials, models, or user-level DotCraft configuration.
- Diagnosing Host or Runtime startup, workspace logs, source code quality, or task completion quality.
- Editing configuration, applying fixes, writing patches, or changing target workspace state.
- Replacing the Web Dashboard or reproducing its settings, usage, Dreams, Automations, and mutation features.
- Connecting to a remote AppServer or defining a live attachment protocol.
- Editing, deleting, repairing, or migrating workspace data.
- Shipping DotCraft Trace Viewer as an installed DotCraft product.

## Core design

### Application boundary

DotCraft Trace Viewer is a sample, not a new DotCraft composition root. It does not join the cross-platform `dotcraft.sln` build or DotCraft product release artifacts. Windows development uses `src/DotCraft.TraceViewer/DotCraft.TraceViewer.sln`, while local publishes target the application project directly.

The application follows the unpackaged WinUI lifecycle used by the repository's Windows tooling:

- `net10.0-windows10.0.19041.0`
- WinUI 3 with `WindowsPackageType=None`
- self-contained `win-x64` publish
- single-file publish where supported by the Windows App SDK
- no Release PDB output

The Trace Viewer and Trace Analyst have separate ownership boundaries. Opening a workspace starts only the read-only Viewer. DotCraft Trace Viewer creates the Analyst Host lazily after the user explicitly starts an analysis. The Analyst Host is stopped when its workspace context is replaced or the application closes.

### Appearance

The workspace action area exposes one application-level Appearance setting with `System`, `Light`, and `Dark` values. It remains part of application content rather than competing with Windows caption controls. `System` follows the current Windows application theme. `Light` and `Dark` override the root visual theme without changing the operating-system setting. Changes apply immediately to every Trace Viewer surface and persist under application-owned settings for the next launch. Windows minimize, maximize, and close controls update their foreground and interaction colors with the effective application theme.

Appearance is independent of the selected workspace. Opening a workspace never reads or writes its theme configuration, and switching workspaces does not change the selected appearance.

Native WinUI fields, selection indicators, flyouts, and focus treatments use the DotCraft accent instead of the current Windows accent. Selection rows and menu choices keep the same neutral hover and selected surfaces as the application-owned Appearance choices. Vertical scrolling regions reserve a stable right-side gutter so an expanded scrollbar never covers row content.

### Trace source

The user selects a workspace root. DotCraft Trace Viewer first checks `.craft/state.db`. When it is absent, the viewer accepts exactly one direct-child directory containing `state.db`. No matching directory and multiple non-default matches are explicit non-destructive errors. The resolved directory is validated with the existing DotCraft path contract, then opened through `WorkspaceStateDatabase` in read-only mode with a read-only `TraceStore`.

Opening or refreshing a workspace must not create DotCraft directories, initialize schemas, checkpoint the database, or change application data. SQLite may create or maintain its standard `-wal` and `-shm` coordination files while a WAL database is open; DotCraft Trace Viewer never deletes or manages those files. Application preferences such as the most recent workspace belong under the current user's local application-data directory.

### Trace analysis boundary

Starting an analysis captures the selected Session into an immutable in-memory snapshot, then exports that snapshot as a revision-owned Evidence Bundle under DotCraft Trace Viewer application data. The bundle contains the complete recorded Trace content available at that revision in UTF-8 files suitable for the standard DotCraft file tools. Later commits to the target database do not alter the active analysis or its evidence references.

The bundle uses stable hashes of the normalized workspace path, Session key, and Trace revision as directory names. Its manifest identifies the Session, revision, generation time, last activity, and Event count without including the target workspace path. A chronological JSON Lines index maps every exact Event id to an ordinal detail directory. Each detail directory contains the structured Event fields and separate bounded chunks for large content, tool arguments, tool results, metadata, and final system prompt fields. A completed revision is never overwritten in place.

The Trace Analyst runs in an application-owned workspace under the current user's local application-data directory. Its Runtime state, Threads, and latest Review records never use the target workspace's `DataPath`. Analyst tracing is disabled so the analysis does not create a second trajectory that could be mistaken for target evidence.

DotCraft Trace Viewer loads the Analyst's effective model configuration from the selected workspace and DotCraft user configuration. It disables tracing, MCP servers, plugins, and hooks for the isolated Analyst Runtime. The application does not expose provider or credential management. If no usable provider is configured, Review shows an actionable unavailable state and does not start a model request.

DotCraft Trace Viewer embeds a dedicated `trace-review` built-in Skill in its own assembly and deploys it into the Analyst workspace with the standard DotCraft built-in Skill lifecycle. The Skill owns the investigation workflow, review dimensions, severity and evidence rules, and Evidence Bundle layout. It is preloaded for every Analyst Thread and is not added to the global DotCraft Core Skill set.

The Analyst uses standard read-only DotCraft file capabilities against the current Evidence Bundle. Its complete model-visible tool surface is:

- `ReadFile`
- `FindFiles`
- `GrepFiles`
- `SubmitTraceReview`

The Analyst Thread belongs to the Trace Viewer analysis workspace. Its working directory and runtime workspace roots are both set to the current Evidence Bundle, and access outside that boundary is rejected rather than approved. Provider services may use the configured DotCraft user-data directory for authentication, but that directory is explicitly blacklisted from Agent file access. The Analyst receives no Shell, file mutation, MCP, Plugin, Hook, SubAgent, or Agent-control capability.

Large Trace fields are split into bounded files that the standard `ReadFile` capability can read in full without replacing, sanitizing, or semantically filtering their content. The chronological index only locates Event detail directories. Each `event.json` lists the bounded files that contain its large fields. The Agent first reads the manifest and index, then opens only the Event details required by its investigation. The model-visible contract depends on the bundle layout and standard file tools rather than a Trace-specific query API or external script runtime.

`SubmitTraceReview` is the only successful completion path for an initial Review. The model submits only the summary and structured Finding inputs. DotCraft Trace Viewer supplies the schema version, Session, revision, generation time, model, and Analyst Thread identity. It validates severity, basis, dimension, and every evidence reference against the immutable snapshot before accepting the result. Invalid input returns a structured `review_rejected` result without throwing an application exception. A rejected submission remains inside the Agentic Loop so the Analyst can correct it and submit again.

### Review contract

A Review contains one concise summary, zero or more Findings, the analyzed Trace revision, the selected model, the generated time, and the Analyst Thread used for follow-up conversation. It does not assign a synthetic score to the Session.

Finding severity uses the same review language as Oratorio:

- `Major` identifies an explicit failure or a high-confidence problem with material reliability, latency, or efficiency impact.
- `Minor` identifies a localized problem or operational risk with observable impact.
- `Suggestion` identifies a supported optimization opportunity that did not cause a current failure.

Every Finding belongs to exactly one dimension:

- `Reliability` covers Provider, Response, Turn, and Runtime errors, retries, and abnormal termination.
- `Latency` covers Turn duration, model attempts, tool duration, and significant inactive intervals.
- `Tool behavior` covers repeated calls, failed calls, unchanged retries, and abnormal invocation density.
- `Token efficiency` covers context growth, fresh input, reasoning, and repeated token consumption.
- `Prompt cache` covers cache-hit changes, cache breaks, and prompt or tool-schema drift.

Missing Trace evidence, incomplete Turns, unmatched tool events, and other evidence-quality constraints must not be promoted into Findings. The Analyst does not judge whether the user's original task was completed correctly because Trace alone does not provide an authoritative acceptance criterion.

Each Finding contains a stable id, severity, dimension, title, explanation, impact, recommendation, `Confirmed` or `Inferred` basis, and at least one evidence reference. Evidence identifies one recorded Event or an inclusive Event range in the selected Session. Findings sort by Major, Minor, and Suggestion, then by their earliest evidence.

The latest successful Review is stored with its immutable Trace snapshot and Analyst conversation as one atomic record under `~/.craft/trace-viewer/sessions`. The path uses stable hashes of the normalized workspace path and Session key, so absolute paths are not used as filenames. Unreadable stored data is treated as an unavailable Review. When the matching Evidence Bundle is absent, the Viewer rebuilds it from the stored snapshot before a follow-up Turn.

A Trace revision contains enough stable identity to detect a changed event window, including the latest event identity, event count, and last activity. When the current Trace differs, the stored Review remains readable but is marked `Trace updated`. A successful re-analysis replaces the previous Review and conversation binding. Cancellation or failure leaves the previous Review intact.

### Trajectory projection

The projection preserves the recorded event order and never fabricates missing duration, token, turn, or relationship data.

- `TurnCompleted` closes the current turn group.
- Events after the most recent completed turn form a visible in-progress group.
- Provider requests within a turn use recorded request and LLM-call indexes when available. The ledger exposes this as a `Turn → model call → event` hierarchy without changing chronological order.
- Tool start and completion events correlate by call ID when possible. An unmatched event remains independently visible.
- Maintenance, compaction, cache, provider, rollback, and error events remain explicit diagnostic rows rather than being folded into assistant text.
- Timing blocks appear only when the required timestamps or recorded durations exist.

The initial view opens at the newest page. When an older page is available, reaching the start of the event ledger requests it automatically and a quiet first-row action remains as an accessible fallback. Loading older pages prepends events while preserving the selected event and visible scroll anchor when possible. Search and filters operate on the currently loaded event window and make that boundary visible; paging does not occupy the search toolbar.

### User interface

The window title bar contains one sharp vector product icon and the product name. It does not repeat the product name as a subtitle when no workspace is open.

The window has two application states rather than several top-level product areas:

1. Workspace and Sessions presents workspace selection, summary information, search, and a virtualized session catalog.
2. Session workbench presents one Session with `Timeline` and `Review` as peer modes.

The analysis state uses an adaptive Sessions pane. Wide windows keep it inline, while medium and narrow windows expose it as an overlay. Event details are not a permanent third column. Selecting an event or using the explicit Details action opens a drawer from the right at every window width. The drawer inherits the analysis surface and is separated only by its left boundary. Closing it restores the full ledger without changing selection.

The chronological ledger is the canonical accessible event representation. It projects a collapsible `Turn → model call → event` hierarchy while preserving native list selection and chronological reading order.

A compact, unframed overview projects Input, Model, and Tools lanes. Tool lifecycle, tool availability, and Skill loading activity share Tools rather than creating a separate Runtime lane; context/system activity joins Input and model-owned maintenance joins Model. Its normal markers use the restrained DotCraft accent and theme-specific neutral tones whose contrast is verified independently in Light and Dark. Warning and error colors are reserved for recorded warning and error states rather than event categories. The existing scale selector presents Sequence on the left and Duration on the right. Equal-width Sequence mode is selected by default so tightly clustered work remains inspectable; Duration positions recorded points and spans on elapsed time when latency inspection is useful. The overview supports bounded zoom, middle-button drag panning, and primary-button drag range marking. It shows Turn boundaries derived from recorded events and never fabricates a duration for point events. Selecting a marker selects and reveals the corresponding ledger row, while selecting a ledger row highlights the corresponding marker. The overview does not replace keyboard navigation or UI Automation.

The event inspector opens on demand. Wide windows allow its width to be adjusted within a bounded range. The resize target remains eight pixels wide but renders as the standard DotCraft neutral boundary, with a center-weighted boundary highlight on hover and drag rather than a visible grip. Medium windows show it as an overlay, and narrow windows use the available analysis width. Empty, invalid, locked, and schema-incompatible workspaces render actionable non-destructive states.

### Review experience

Review uses the complete workbench content area rather than the Event Details drawer. The workbench header remains stable while users switch between `Timeline` and `Review`.

Before analysis, Review uses a centered empty state that identifies the selected provider and model, briefly states that the Session's complete Trace content will be sent to DotCraft, and offers `Analyze trace`. It does not show the ready-Review status header or empty severity counts. Clicking that action is the explicit consent and trigger. There is no additional confirmation dialog and no automatic analysis.

During analysis, the empty state becomes an indeterminate activity state. It shows the current observable phase, such as preparing Trace evidence, starting the Analyst, scanning the Evidence Bundle, inspecting Event files, or validating Findings, together with Stop. Standard file-tool lifecycle events drive the scanning and inspection phases. It never exposes model reasoning, invents a percentage, or claims a phase has completed before the corresponding application or tool action occurs.

Review supports these visible states: `Not analyzed`, `Analyzing`, `Ready`, `Trace updated`, `Cancelled`, `Failed`, and `Provider unavailable`. Analyzing and follow-up Turns expose an explicit Stop action. Only one Analyst Turn runs at a time in the application.

A ready Review presents, in order:

1. status, model, generated time, severity counts, and `Re-analyze`;
2. summary;
3. Findings;
4. evidence-grounded conversation, only after the first follow-up message exists;
5. a sticky composer.

Finding severity uses a compact, vertically centered semantic badge. `Major` uses critical colors, `Minor` uses caution colors, and `Suggestion` uses informational colors. The badge remains a small status accent rather than a large filled surface.

The composer may attach the current Finding, selected Event, or marked Timeline range to the next question. Follow-up questions reuse the Review's Analyst Thread and immutable snapshot. They are a bounded investigation of the selected Session, not a general Agent chat surface.

Selecting evidence preserves Review context and highlights the corresponding Timeline marker and ledger row. Event Details may open temporarily without replacing the Review. `Show in Timeline` switches modes while retaining the Finding and evidence highlight. Valid `trace://event/{eventId}` links in Analyst answers navigate through the same selection path. Invalid references remain inert text and are never presented as verified evidence.

### Session identity

The primary session title is derived from the first user request for display only. Persisted Trace content is never rewritten.

Because `FirstUserRequest` may contain Runtime Context appended for the model, the display formatter may remove one final `<system-reminder>` block only when it reaches the end of the request and contains the complete DotCraft Runtime Context signature: Environment, date, time zone, Mode, current mode, and Mode Action sections. It must not remove middle blocks, arbitrary reminder-like user text, or truncate at an unclosed tag.

After removal, the formatter collapses whitespace and produces a compact row title while retaining the complete cleaned value for the session header. When no display text remains, it uses a date-based fallback and keeps the session key as secondary metadata.

### Event presentation

The default Activity projection prioritizes user requests, model responses, tool activity, token usage, runtime changes, and errors. Low-level provider, cache, and request-shape diagnostics remain available through Diagnostics, All events, and Raw views without dominating the primary execution story.

Tool start and completion events correlate by call ID into one logical activity when possible. Correlation quality remains visible when one side is outside the loaded page. Unknown future event types use a generic row and Raw representation rather than disappearing.

Event detail is semantic. Applicable Overview, Content, Context, Diagnostics, and Raw sections are selected according to event type. The drawer is one elevated surface with frameless sections. Structured fields use stable label/value columns, prose wraps naturally, and raw or code-like payloads use one selectable neutral code surface without a second decorative card. The interface does not label system prompts, session metadata, token metrics, or diagnostic records as generic Input or Output.

## Lifecycle and failure behavior

- The application opens with no workspace and performs no DotCraft filesystem access until the user selects one.
- Switching workspaces disposes the previous database and trace store before opening the next source.
- Opening or selecting a Session never starts the Analyst or sends model data.
- Starting analysis snapshots the selected Trace revision before the first model request.
- Switching workspace or closing the application cancels active analysis and converges Analyst Host shutdown.
- Cancelling, failing, or losing provider access does not replace the latest successful Review.
- A Trace update marks the Review stale without silently changing its evidence set or conversation context.
- A workspace may be inspected while another DotCraft process is writing to it. Refresh re-queries committed data and does not claim protocol-level live attachment.
- Missing or ambiguous `state.db`, an invalid resolved `DataPath`, unsupported schema, and read failures do not terminate the application.
- Closing the window disposes all database resources and leaves the selected workspace unchanged.
- Loading an older event page preserves the selected event and visible anchor.
- Refresh follows new tail events only when the user was already near the tail.

## Dependency contract

During repository development, the sample uses a local `ProjectReference` to `DotCraft.Harness` and explicitly references the .NET Generic Host package. The aggregated package supplies the Runtime, Core, Agents, and built-in providers while preserving their assembly boundaries. DotCraft Trace Viewer does not duplicate the SQLite schema or deserialize database rows independently. Public installation guidance uses the `DotCraft.Harness` NuGet package after publication.

## Verification

- Build and publish the WinUI project on Windows x64.
- Open trace databases produced with both `.craft` and `.agents` data directories.
- Verify session ordering, paging, turn grouping, tool correlation, diagnostics, filters, selection, and timing against deterministic fixtures.
- Open a workspace while DotCraft is writing traces and verify manual refresh observes committed additions.
- Confirm the primary database and non-SQLite workspace data remain unchanged before and after inspection.
- Use a scripted provider to verify built-in Skill preloading, Evidence Bundle discovery, standard file reads, structured Review submission, evidence validation, and follow-up conversation.
- Verify latest-Review persistence, stale detection, cancellation, failure retention, invalid stored data, and provider-unavailable behavior.
- Verify long Trace content remains fully retrievable through bounded Evidence Bundle files without requiring one oversized prompt.
- Confirm analysis never adds files or records to the target workspace.
- Confirm Release publish output contains no PDB files.
- Verify System, Light, and Dark apply immediately and survive application restart without touching the selected workspace.

## Acceptance checklist

- DotCraft Trace Viewer runs as an unpackaged Windows x64 application.
- A developer can select a workspace and inspect its Trace without starting a server.
- Long sessions remain usable through paging and UI virtualization.
- Session titles do not expose appended DotCraft Runtime Context or broadly remove user-authored reminder-like text.
- Recorded requests, responses, tools, errors, token usage, compaction, and cache diagnostics are distinguishable.
- The selected workspace receives no writes from the viewer.
- Analysis begins only after explicit user action and names the configured provider and model.
- A successful Review contains only validated, evidence-linked Findings.
- Review and Timeline preserve a shared evidence selection across mode changes.
- Follow-up conversation remains grounded in the selected snapshot and can navigate valid evidence links.
- The latest successful Review survives restart and is visibly stale after the target Trace changes.
- The Analyst exposes only `ReadFile`, `FindFiles`, `GrepFiles`, and `SubmitTraceReview`, preloads only the Trace Viewer-owned `trace-review` Skill, and writes only to its isolated application data and `~/.craft/trace-viewer/sessions`.
- Appearance offers System, Light, and Dark from the workspace action area, applies immediately, and persists independently of the workspace.
- The sample contains no target Agent execution, provider management, Host-startup diagnosis, or workspace mutation.

## Open questions

None. The durable behavior and product boundaries are defined for implementation.
