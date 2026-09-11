# Remote Screen View

| Field | Value |
|---|---|
| Version | 0.2.0 |
| Status | Draft |
| Date | 2026-09-11 |
| Parent | [Remote Tool Host](../architecture/remote-tool-host.md) |
| Related Specs | [Hub Architecture](../architecture/hub-architecture.md), [Satellite](../clients/satellite.md), [Desktop Client](../clients/desktop-client.md), [Runtime Module Boundaries](../architecture/runtime-module-boundaries.md) |

## 1. Purpose

Remote diagnosis needs the operator to see the remote machine's desktop while a thread is routed to
that machine, not to ask the person at the keyboard what is on screen. This specification defines
Remote Screen View: a live, read-only, latest-frame-only picture of a paired Remote Tool Host's
desktop, carried over the same Hub bridge that carries tool sessions and shown inside the Desktop
conversation as a floating dock and a theater.

Capture is client behavior and belongs to DotCraft. Relaying frames to a fleet and showing them in
a web client is server behavior and belongs to the product that owns the server. Universe therefore
consumes DotCraft's capture and frame format and keeps its own relay and viewer.

The key words **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are normative.

## 2. Boundaries and non-goals

- Screen view is view only. There is no remote input, no cursor, no clipboard, no audio, and no
  file transfer on this channel.
- Nothing is recorded. Neither end persists a frame; the Hub relays and forgets.
- Frames are lossy and non-durable: a viewer sees the newest frame that reached it, never a replay.
- Screen view is not a tool. The model cannot invoke it, it produces no Session item, and it takes
  no workspace lease.
- v1 captures the whole virtual desktop. Per-display capture is an additive extension that adds a
  display selector to the control message; it does not change the frame format.
- Screen view MUST NOT carry a credential, a path, or command output. It carries pixels, a small
  control vocabulary, and stable reason codes.

## 3. Ownership

| Concern | Owner | Package |
|---|---|---|
| Platform capture: `DotCraft.Screen` | `DotCraft.Core` | Harness |
| Frame format, control records, budget clamp: `DotCraft.Protocol.ScreenView` | `DotCraft.Protocol` | Sdk |
| Session kind, host-side session, viewer count, status | `DotCraft.RemoteTools` | Harness |
| Bridge `kind`, capability advertisement, refusal codes | Hub in `DotCraft.App` | application |
| Viewer transport, dock, theater | Desktop | client |
| Fleet relay and web viewer | Universe | Universe |

`DotCraft.Screen` MUST NOT depend on `DotCraft.Protocol`; the Harness package carries no Protocol
assembly. `DotCraft.Protocol.ScreenView` is outside the AppServer contract generator's scope and
contributes nothing to the AppServer manifest. `DotCraft.RemoteTools` depends on both.

## 4. Capture contract

`DotCraft.Screen.IScreenCaptureSource` samples on demand:

```text
Probe()                       -> ScreenCaptureCapability(Available, UnavailableReason)
Capture(MaxWidth, Quality)    -> ScreenCaptureResult(Frame | UnavailableReason, Detail?)
Frame                         =  (Width, Height, Jpeg)
```

`ScreenCaptureSource.Create()` returns the platform backend, or a source whose every answer is
`noCaptureBackend` where the build has none. Windows uses GDI: the virtual desktop is copied into a
top-down 32-bit device-independent bitmap and encoded with ImageSharp. No other imaging dependency
is introduced.

Rules:

- The capture unit is the whole virtual desktop, starting at its real origin, which may be negative.
- `MaxWidth` bounds width because a desktop is wider than tall and horizontal room is what makes a
  frame readable; height follows the aspect ratio and is never zero.
- A source is safe for concurrent `Capture` calls and reuses its bitmap and pixel buffer until the
  desktop bounds change, so one source serves every consumer in a process. The screen device context
  is acquired for each capture and released after it: a context cached across a session switch, a
  lock, or a remote-desktop reattach stops copying and never recovers.
- A copy that fails is retried once on a freshly created bitmap before the capture is reported as
  failed, because the ordinary cause is a stale surface, not a missing desktop.
- Unavailable displays are state, not faults. The reasons are a closed set:

| Reason | Meaning |
|---|---|
| `noCaptureBackend` | This platform has no capture implementation in this build. |
| `noInteractiveSession` | No input desktop is open to read, such as a locked machine. |
| `noDisplayServer` | There is no display at all, the ordinary case on a headless host. |
| `captureFailed` | The platform refused the capture for a reason the host cannot resolve. |

- `captureFailed` MAY carry a `Detail`: one line of at most 200 characters naming the platform
  error, such as `BitBlt failed (Win32 6)`, for diagnosis only. It MUST NOT contain a path, and
  no viewer shows it as status.
- Protected surfaces and hardware overlays may appear black. That is a platform limit, and this
  specification does not work around it.
- The backend sets per-monitor DPI awareness for the process on creation, best effort, so a process
  without an application manifest still reads physical pixels.

## 5. Frame format

A frame is one binary WebSocket message: a 16-byte little-endian header followed by the JPEG.

| Offset | Type | Field |
|---|---|---|
| 0 | `uint32` | `sequence`, wrapping |
| 4 | `uint16` | `width` |
| 6 | `uint16` | `height` |
| 8 | `int64` | `capturedAtUnixMs` |
| 16 | bytes | JPEG |

The sender MAY send the header and the JPEG as two fragments of one message so the frame is never
copied into a joined buffer. A relay MUST preserve message type and fragment boundaries. A reader
MUST honor the header's dimensions only when both are positive and their product does not exceed
`8192 × 8192`; a header past that ceiling describes an allocation, not a desktop.

Ceilings, identical on every end:

| Constant | Value |
|---|---|
| `MaximumFrameBytes` | 2 MiB |
| `MaximumFramePixels` | 8192 × 8192 |
| `DefaultFps` | 8 |
| `Fps` | 1 to 15 |
| `MaxWidth` | 320 to 3840 |
| `Quality` | 20 to 90 |

A frame that encodes past `MaximumFrameBytes` is re-encoded at quality 40, then at half the width
(never below 320) and quality 40; one that still does not fit is dropped without a capability
message, because the next capture supersedes it before a reassembled one could be drawn.

Text messages on the same socket are JSON with camelCase members:

| Direction | Record | Meaning |
|---|---|---|
| viewer → host | `ScreenViewControl { watchers, fps, maxWidth, quality }` | The viewer's demand. Every transition is pushed; `watchers = 0` stops capture entirely. The host clamps every field to the ceilings above. |
| host → viewer | `ScreenViewCapability { enabled, unavailableReason, detail }` | Sent once when the session opens and again whenever capture stops or resumes. `enabled` is whether the host allows viewing at all, always true from a DotCraft host, which refuses the session instead; `unavailableReason` is one of the §4 reasons or null and alone decides whether frames can be expected; `detail` is the §4 diagnostic line or null. |

A host reports `captureFailed` only after three consecutive captures fail: the first two are retried
at the frame cadence, and only the third is a state change worth telling the viewer. Once reported,
the host keeps trying at the slower unavailable cadence, and the first frame that succeeds is
preceded by a capability message with no reason. Every other reason is reported as soon as it is
observed. The host sends a capability message only when reason or detail changed.

## 6. Transport

### 6.1 Session kinds

The Hub bridge and the satellite data route carry sessions of two kinds. `tools` is the MCP session
defined by [Remote Tool Host](../architecture/remote-tool-host.md) §8. `screen` is one screen view.
The relay is kind-blind: it never learns which it carries.

A Remote Tool Host declares the kinds it can serve in `hello` as `capabilities`; the only screen
capability in v1 is `screen-v1`, declared when the build has a capture backend. The Hub MUST NOT ask
a peer to open a kind it did not declare, and it reports each online peer's capabilities to local
clients so a viewer control appears only where it can work.

### 6.2 Opening a view

```text
viewer  -> Hub   GET ws://<hub>/v1/satellites/{peerId}/bridge?session=<id>&kind=screen
                 Authorization: Bearer <Hub token>
Hub     -> host  openSession { sessionId, sessionKind: "screen" }
host    -> Hub   GET ws://<hub>/satellite/data?peer=<peerId>&session=<id>   (peer credential)
host    -> viewer  ScreenViewCapability
viewer  -> host    ScreenViewControl
host    -> viewer  frames while watchers > 0
```

`kind` absent means `tools`. The Hub answers an unknown kind with `400 sessionKindUnsupported`, an
unknown peer with `404 satelliteNotFound`, a paired peer without a live control connection with
`503 satelliteOffline` for either kind, and a peer that did not declare `screen-v1` with
`409 satelliteScreenUnsupported`; the offline answer comes before the capability check because a
peer declares its capabilities only while connected. It keeps its existing answers for a duplicate
session id and a missing upgrade. A viewer treats `503` as a machine to dial again, never as a
machine that cannot be watched.

A host that cannot open the view answers `sessionFailed` with a stable code, and the Hub closes the
viewer's socket with that code as the close description. A host that ends a live view closes its
data socket with the code as the close description, and the relay carries the close through.

| Code | Sent when |
|---|---|
| `sharingPaused` | The machine owner paused sharing, before or during the view. |
| `authorizationRequired` | The pairing has no valid authorization mode. |
| `hostClosed` | The host ended the view for any other reason: shutdown, disconnect, revoke, authorization change, or a lost Hub connection. |
| `satelliteOffline` | Hub: the peer has no live control connection. |
| `satelliteSessionFailed` | Hub: the peer did not open the data connection in time, or failed without a code. |

A viewer treats `sharingPaused` as a paused view that may resume, `authorizationRequired` as one
that needs the machine owner, and every other code as a connection to retry with backoff.

### 6.3 Flow control

Latest-frame-only is enforced at both endpoints, because the relay cannot skip:

- the host captures nothing while `watchers <= 0`, captures at most one frame per `1 / fps`, and never
  starts a capture while a send is outstanding;
- a viewer keeps at most one undecoded frame and overwrites it until the decoder is free.

A view has no heartbeat of its own; the WebSocket keep-alive and the control channel's presence
decide liveness. A viewer that stops receiving frames keeps showing its last frame and reports the
stall rather than blanking.

### 6.4 Leases and status

A screen session takes no workspace lease and holds no lease-owned resource. It counts as a data
session for the host's status: a host with an open view and no lease reports `connected`, so the
machine owner's island is visible for the whole time the machine is watched. The number of open views
per pairing is exposed on the peer record as `screenViewers`, and the host raises a start event and a
stop event on each pairing's transition between zero and more than zero.

Ending a screen session MUST NOT affect a tool session on the same pairing, and vice versa. Pause,
disconnect, revoke, authorization change, and host shutdown end both, through the same drain that
already ends tool sessions.

## 7. Consent and visibility

Viewing is enabled for every pairing whose authorization is valid while sharing is not paused. There
is no per-pairing setting and no per-view approval in v1: pairing already lends the machine, and the
machine owner's controls are the existing ones — pause, disconnect, revoke.

The machine owner MUST be able to tell that the machine is being watched:

- for the whole time `screenViewers > 0` the island's glyph becomes an eye, in the same tone as the
  dish, and each watched machine's detail row says so in words, without changing the island's mode
  or using colour;
- an operating-system notification is raised when a pairing starts being watched and when it stops.

Nothing about a view is persisted on either machine, and no frame is written to disk or to a log.

## 8. Desktop viewer

Desktop shows a view for the thread's routed machine. The transport is a third plane beside the two
in [Desktop Client](../clients/desktop-client.md) §6.11:

- the Desktop main process dials the Hub bridge with the Hub bearer read from `hub.lock`; the bearer
  never enters the renderer, and the AppServer is not involved;
- frames reach the renderer over a main-owned channel with a credit of one: main holds at most one
  undelivered frame per view and overwrites it until the renderer acknowledges a decode;
- a view exists only while the dock or theater is open on a visible window. Hiding or minimizing the
  window sends `watchers = 0`; showing it again resumes. Closing the view closes the socket.

The view has seven states: `connecting`, `live`, `stalled`, `reconnecting`, `paused`, `offline`,
`unavailable(reason)`. `paused` and `offline` follow the close codes and the `503` answer in §6.2;
`unavailable` follows `ScreenViewCapability`. A view that lost its frames keeps the last one.

The view never says its state in words. The picture carries it, and the words exist only in the
surface's tooltip and accessible name, as `<machine> · <state>`, with the host's `detail` appended
for `unavailable`:

| State | Surface |
|---|---|
| `live` | the picture alone |
| `connecting` | a dark stage with the satellite glyph breathing |
| `stalled`, `reconnecting` | the last frame, dimmed, with one pulsing dot in a corner |
| `paused`, `offline`, `unavailable` | the last frame, dimmed further, with the satellite glyph still at the centre; paused marks the glyph as paused |

Reduced motion stops the breathing and the pulse. The launcher's icon takes the accent colour only
while the window's view is `live`.

The launcher opens a view when the thread has a route, the routed machine is connected, and it
declared `screen-v1`; a route whose lease was lost counts as not connected. Once open, a view stays
open through `offline` and `reconnecting`, redialing until the machine returns, and closes only when
the route is removed or the person closes it; the launcher stays visible for as long as the view is
open. One stream is open per window at a time; switching threads closes it and remembers the view's
mode per thread, and a remembered view reopens only when the machine is connected.

The dock is a picture-in-picture surface: it floats above the window's content, outside any column,
and the person places it anywhere inside the window. The whole picture drags, a grip resizes it, and
its width and position are remembered across restarts and clamped inside the window below the title
bar. At rest the dock is the picture alone; the machine's name, expand, and close appear on hover or
keyboard focus and leave with the pointer. The theater is a modal centred in the window, as large as
the stream's shape allows under the title bar, whose chrome follows the same rule, and returning to
the dock does not reconnect.

Neither surface is sized by the pixels of the frame it is showing. Both take their box from the
window and the stream's aspect ratio and scale the picture into it, so a frame that arrives at a new
size changes only what is drawn. A surface whose size decided the next requested width, which decided
the next frame's size, would resize itself once per round trip for as long as it took to converge.

The requested width follows the pixels the surface can show: the surface's CSS width times the
device pixel ratio, rounded up to a multiple of 64 and clamped to the §5 `MaxWidth` range, re-sent
when the surface is resized or moved to a display with another ratio. `fps 8`, `quality 82` on both
surfaces.

## 9. Reuse by other products

A product that hosts DotCraft in process reuses `DotCraft.Screen` for capture and
`DotCraft.Protocol.ScreenView` for the header, the ceilings, the control and capability records, and
`Clamp`. It owns its relay, its viewer, its consent model, and any status record its relay reports.
It MUST keep the frame format byte-identical so a viewer written against one product reads frames
from the other.

## 10. Conformance

- header round-trip and rejection of short, zero-sized, and oversized headers; `Clamp` bounds every
  field and floors `watchers` at zero;
- the ScreenView namespace adds no type to the AppServer manifest;
- a platform without a backend reports `noCaptureBackend` from both `Probe` and `Capture`; `Fit`
  keeps aspect and never yields zero;
- the Hub relay preserves a two-fragment binary message in both directions;
- `openSession` carries `sessionKind`; an unknown kind is refused with `400`; a paired peer that is
  not connected with `503` before any capability check; a peer without `screen-v1` with `409`;
- against a running host with a fake source, frames flow, capture stops at `watchers = 0`, and the
  host reports `connected` while watched and `standby` afterwards with no lease taken at any point;
- one failed capture sends the viewer nothing; the third in a row sends `captureFailed` with its
  detail once, and the first frame after it is preceded by the recovered capability;
- a frame still over `MaximumFrameBytes` at quality 40 is re-encoded at half width;
- pausing ends a live view with `sharingPaused` in the viewer's close description, and a new
  `kind=screen` dial while paused is refused with the same code;
- the island marks each watched machine without changing its mode; the watching strings exist in
  every Satellite locale;
- the Desktop frame reader honors a non-zero buffer offset; the session backs off, reconnects,
  redials a `503` every ten seconds without giving up, and stops reconnecting on a terminal
  capability; hiding the window pauses capture; the launcher follows the opening rule in §8;
- an open view survives the machine going offline and closes when its route is removed; the
  requested width follows the surface's pixel width in steps of 64 inside the `MaxWidth` range;
- the dock moves where it is dragged, stays inside the window, keeps its right edge while the grip
  resizes it, and renders no state text while `live`; its accessible name carries the state;
- opening the theater asks for one width, and frames arriving at other sizes neither resize a
  surface nor ask for another.
