# Remote Screen View

| Field | Value |
|---|---|
| Version | 0.9.1 |
| Status | Draft |
| Date | 2026-10-19 |
| Parent | [Remote Tool Host](../architecture/remote-tool-host.md) |
| Related Specs | [Hub Architecture](../architecture/hub-architecture.md), [Runtime Module Boundaries](../architecture/runtime-module-boundaries.md) |

## 1. Purpose

Remote diagnosis needs the operator to see the remote machine's desktop while a thread is routed to
that machine. Remote Screen View is a live, read-only, latest-frame-only picture of a paired Remote
Tool Host's desktop, carried over the same Hub bridge as tool sessions and shown in the Desktop
conversation as a floating dock and a theater.

Capture and the frame format belong to DotCraft. A product that relays frames to a fleet or shows
them in its own client consumes DotCraft's capture and frame format and owns its relay and viewer
(§9).

The key words **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are normative.

## 2. Boundaries and non-goals

- View only: no remote input, cursor, clipboard, audio, or file transfer on this channel.
- Nothing is recorded. Neither end persists a frame; the Hub relays and forgets.
- Frames are lossy and non-durable: a viewer sees the newest frame that reached it, never a replay.
- Screen view is not a tool. The model cannot invoke it, it produces no Session item, and it takes
  no workspace lease.
- Capture covers the whole virtual desktop.
- Screen view MUST NOT carry a credential, a path, or command output. It carries pixels, a small
  control vocabulary, and stable reason codes.

## 3. Ownership

| Concern | Owner |
|---|---|
| Platform capture (`DotCraft.Screen`) | Harness |
| Frame format, control records, budget clamp (`DotCraft.Protocol.ScreenView`) | Sdk |
| Session kind, host-side session, viewer count, status | Remote Tool Host |
| Bridge `kind`, capability advertisement, refusal codes | Hub |
| Viewer transport, dock, theater | Desktop |

Capture MUST NOT depend on the Protocol package, because the Harness package carries no Protocol
assembly. The screen-view protocol is outside the AppServer contract and contributes nothing to the
AppServer manifest.

## 4. Capture contract

A capture source samples on demand:

```text
Probe()                       -> ScreenCaptureCapability(Available, UnavailableReason)
Capture(MaxWidth, Quality)    -> ScreenCaptureResult(Frame | UnavailableReason, Detail?)
Frame                         =  (Width, Height, Jpeg)
```

A build without a platform backend returns a source whose every answer is `noCaptureBackend`.
Capture uses the shared [image capability](../architecture/image-processing.md) to resize and encode
JPEG; image-processing failures map to `captureFailed`.

Rules:

- The capture unit is the whole virtual desktop, starting at its real origin, which may be negative.
- `MaxWidth` bounds width; height follows the aspect ratio and is never zero.
- Dimensions are physical pixels, independent of the host application's DPI awareness.
- A source supports concurrent consumers and recovers when desktop bounds, session, lock, or
  remote-desktop attachment change. A stale capture surface must not permanently disable viewing.
- Protected surfaces and hardware overlays may appear black; this is a platform limit.
- Unavailable displays are state, not faults. The reasons are a closed set:

| Reason | Meaning |
|---|---|
| `noCaptureBackend` | This platform has no capture implementation in this build. |
| `noInteractiveSession` | No input desktop is open to read, such as a locked machine. |
| `noDisplayServer` | There is no display at all, the ordinary case on a headless host. |
| `captureFailed` | The platform refused the capture for a reason the host cannot resolve. |

`captureFailed` MAY carry a `Detail`: one line of at most 200 characters naming the platform error,
such as `BitBlt failed (Win32 6)`, for diagnosis only. It MUST NOT contain a path, and no viewer
shows it as status.

## 5. Frame format

A frame is one binary WebSocket message: a 16-byte little-endian header followed by the JPEG.

| Offset | Type | Field |
|---|---|---|
| 0 | `uint32` | `sequence`, wrapping |
| 4 | `uint16` | `width` |
| 6 | `uint16` | `height` |
| 8 | `int64` | `capturedAtUnixMs` |
| 16 | bytes | JPEG |

The sender MAY send header and JPEG as two fragments of one message. A relay MUST preserve message
type and fragment boundaries. A reader honors the header's dimensions only when both are positive and
their product does not exceed `8192 × 8192`.

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
(never below 320) and quality 40; one that still does not fit is dropped silently, since the next
capture supersedes it.

Text messages on the same socket are JSON with camelCase members:

| Direction | Record | Meaning |
|---|---|---|
| viewer → host | `ScreenViewControl { watchers, fps, maxWidth, quality }` | The viewer's demand. Every transition is pushed; `watchers = 0` stops capture entirely. The host clamps every field to the ceilings above. |
| host → viewer | `ScreenViewCapability { enabled, unavailableReason, detail }` | Sent when the session opens and whenever capture stops or resumes. `enabled` is whether the host allows viewing at all (always true from a DotCraft host, which refuses the session instead); `unavailableReason` is a §4 reason or null and alone decides whether frames can be expected; `detail` is the §4 diagnostic line or null. |

A host reports `captureFailed` only after three consecutive failed captures; the first two are
retried at the frame cadence. Once reported, the host keeps trying at a slower cadence, and the
first successful frame is preceded by a capability message with no reason. Other reasons are
reported as soon as observed. A capability message is sent only when reason or detail changed.

## 6. Transport

### 6.1 Session kinds

The Hub bridge and the satellite data route carry two session kinds: `tools`, the MCP session
defined by [Remote Tool Host](../architecture/remote-tool-host.md) §8, and `screen`, one screen view.
The relay is kind-blind.

A Remote Tool Host declares the kinds it can serve in `hello` as `capabilities`; the screen
capability is `screen-v1`, declared when the build has a capture backend. The Hub MUST NOT ask a peer
to open a kind it did not declare, and it reports each online peer's capabilities to local clients
so a viewer control appears only where it can work.

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
`409 satelliteScreenUnsupported`. The offline check comes first because a peer declares capabilities
only while connected. A viewer treats `503` as a machine to dial again, never as one that cannot be
watched.

A host that cannot open the view answers `sessionFailed` with a stable code, and the Hub closes the
viewer's socket with that code as the close description. A host that ends a live view closes its
data socket with the code, and the relay carries the close through.

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

- the host captures nothing while `watchers <= 0`, captures at most one frame per `1 / fps`, and
  never starts a capture while a send is outstanding;
- a viewer keeps at most one undecoded frame and overwrites it until the decoder is free.

A view has no heartbeat of its own; WebSocket keep-alive and control-channel presence decide
liveness. A viewer that stops receiving frames keeps its last frame and reports the stall.

### 6.4 Leases and status

A screen session takes no workspace lease. It counts as a data session for host status: a host with
an open view and no lease reports `connected`, so the owner's indicator stays visible while the
machine is watched. The peer record exposes the number of open views as `screenViewers`, and the host
raises start and stop events on each pairing's transition between zero and more than zero.

Ending a screen session MUST NOT affect a tool session on the same pairing, and vice versa. Pause,
disconnect, revoke, authorization change, and host shutdown end both through the same drain.

## 7. Consent and visibility

Viewing is enabled for every pairing whose authorization is valid while sharing is not paused. There
is no per-pairing setting or per-view approval: pairing already lends the machine, and the owner's
controls are pause, disconnect, and revoke.

The machine owner MUST be able to tell that the machine is being watched:

- while `screenViewers > 0`, the host's status indicator shows a watching glyph and each watched
  machine's detail row says so in words, without changing the indicator's mode or using colour;
- an operating-system notification is raised when a pairing starts and stops being watched.

Nothing about a view is persisted on either machine, and no frame is written to disk or to a log.

## 8. Desktop viewer

Desktop shows a view for the thread's routed machine, independent of AppServer conversations and
tools:

- the main process dials the Hub bridge with the Hub bearer from `hub.lock`; the bearer never enters
  the renderer, and the AppServer is not involved;
- frames reach the renderer with a credit of one: main holds at most one undelivered frame per view
  and overwrites it until the renderer acknowledges a decode;
- a view exists only while the dock or theater is open on a visible window. Hiding or minimizing
  sends `watchers = 0`; showing resumes. Closing the view closes the socket.

The view has seven states: `connecting`, `live`, `stalled`, `reconnecting`, `paused`, `offline`,
`unavailable(reason)`. `paused` and `offline` follow the close codes and the `503` answer in §6.2;
`unavailable` follows `ScreenViewCapability`. A view that lost its frames keeps the last one.

State is shown by the picture, not by words: `live` is the picture alone; `connecting` is an empty
stage with the satellite glyph; `stalled` and `reconnecting` dim the last frame with an activity
mark; `paused`, `offline`, and `unavailable` dim it further with the satellite glyph (marked paused
for `paused`). Words appear only in the tooltip and accessible name, as `<machine> · <state>`, plus
the host's `detail` for `unavailable`. Reduced motion stops animation. The launcher is accented only
while the window's view is `live`.

The launcher opens a view when the thread has a route, the routed machine is connected, and it
declared `screen-v1`; a route whose lease was lost counts as not connected. An open view stays open
through `offline` and `reconnecting`, redialing until the machine returns, and closes only when the
route is removed or the person closes it; the launcher stays visible while it is open. One stream is
open per window; switching threads closes it and remembers the view's mode per thread, and a
remembered view reopens only when the machine is connected.

The dock is a picture-in-picture surface floating above the window's content, placeable anywhere in
the window: the picture drags, a grip resizes, and width and position persist across restarts,
clamped inside the window below the title bar. Its name, expand, and close controls appear only on
hover or keyboard focus. The theater is a centred modal as large as the stream's aspect allows, with
the same chrome rule; returning to the dock does not reconnect.

Neither surface is sized by the frame it shows: each takes its box from the window and the stream's
aspect ratio and scales the picture into it, so a frame at a new size never resizes the surface. The
requested `maxWidth` is the surface's displayed width in device pixels, rounded up to a multiple of
64, clamped to the §5 range, and re-sent when the surface is resized or moves to a display with
another pixel ratio. Both surfaces request `fps 8`, `quality 82`.

## 9. Reuse by other products

A product that hosts DotCraft in process reuses `DotCraft.Screen` for capture and
`DotCraft.Protocol.ScreenView` for the header, ceilings, control and capability records, and
`Clamp`. It owns its relay, viewer, consent model, and any status record its relay reports. It MUST
keep the frame format byte-identical so a viewer written against one product reads frames from the
other.
