# DotCraft Desktop Voice Input Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Living |
| **Date** | 2026-10-19 |
| **Related Specs** | [OpenAI Subscription Auth](../architecture/openai-subscription-auth.md), [Design System](../architecture/DESIGN.md), [Desktop Client](../clients/desktop-client.md) |

Purpose: define speech-to-text input for the DotCraft Desktop Composer.

## 1. Scope

Voice Input lets a user record speech in a DotCraft Composer, transcribe it, and append the transcript to the originating draft. A user signed in with ChatGPT transcribes through the ChatGPT subscription by default; every other user, or a user who turns that route off, transcribes with a DotCraft-managed local Whisper model.

The feature covers:

- Foreground-only recording from thread, main welcome, and Agent Builder welcome Composers, and from desktop pet Quick Chat on behalf of its source Composer.
- Click-to-toggle and fixed hold-to-dictate interaction, with a real signal-driven waveform.
- Local transcription in an isolated inference worker, and ChatGPT subscription transcription from Electron Main, with a Settings switch that keeps all audio on the device.
- One managed multilingual model with automatic language detection, and its install, validation, repair, cancellation, and removal.
- Microphone selection and permission recovery in Desktop settings.
- Background transcription and writeback after thread navigation, a bounded queue, and retry after failure.

The feature is owned by Desktop. Audio, model state, and transcription jobs do not cross the AppServer boundary; the ChatGPT route only reads a short-lived access token from the connected AppServer.

## 2. Principles

- **Subscription first, local always available.** With a ChatGPT sign-in and the ChatGPT switch on, recorded audio is sent only to ChatGPT's transcription service. Otherwise it is processed on the device. Audio is never sent to AppServer or any other speech service.
- **Composer-native.** Voice Input adds text only to a DotCraft Composer and never injects text into other applications.
- **Draft-safe.** Recording, transcription, navigation, and failure never destroy or replace existing draft content.
- **Quiet feedback.** Composer state uses existing inline controls without status copy, percentages, or success notifications.
- **Two managed routes.** ChatGPT transcription and the managed Whisper model are the only routes. The ChatGPT switch is the only choice exposed; there is no provider, language, or model selection.
- **Bounded work.** One queue serializes transcription on either route, and Desktop accepts at most two unresolved voice sessions.
- **Agent independence.** Voice Input does not change Agent execution, stop, queue, or send semantics.
- **User-facing language.** Product copy describes the available action or recovery and does not expose worker, PCM, provider, model-taxonomy, or queue terminology.

## 3. Non-goals

- Voice chat, text-to-speech, or live conversational audio.
- Other cloud speech providers, API-key speech services, or provider selection.
- AppServer, SDK, CLI, Channel, or protocol methods for speech-to-text.
- Streaming dictation, streaming partial transcripts, or model-based transcript cleanup.
- Automatic fallback to the local model after a ChatGPT transcription failure.
- Linux support.
- Custom model import, model selection, model updates, or manual language selection.
- Global background dictation or text injection into another application.
- Customizable shortcuts.
- Dictation dictionaries, recording or transcript history, input-level testing, or retaining successful recordings.

## 4. Architecture boundary

| Layer | Responsibility |
|-------|----------------|
| Renderer capture | Requests the selected microphone after Main reports the OS permission state, captures mono PCM, and feeds the waveform and the Voice Coordinator. |
| Renderer Voice Coordinator | Owns recording and session UI state across route changes and writes completed transcripts to the originating draft. It lives above the thread route, so unmounting a Composer never loses a result. |
| Electron Main voice service | Owns microphone permission status and recovery, model lifecycle, queue admission, temporary audio, worker lifecycle, and retry state. |
| Inference worker | Runs in an isolated Electron Utility Process, loads the managed model through whisper.cpp, and performs one transcription at a time. |
| ChatGPT transcription client | Runs in Electron Main, reads an access token from the connected AppServer for each request, and uploads the session audio (§6). |

Electron Main is the trust boundary:

- Renderer never receives model or temporary-audio filesystem paths, the ChatGPT access token, or the account id.
- The inference worker never accesses thread state or Desktop settings.
- AppServer does not receive audio, transcripts, progress, or model state.
- Speech-provider types stay behind DotCraft-owned adapters and never appear in the Desktop voice boundary.
- The ChatGPT route uses the ChatGPT sign-in of the connected AppServer host; with a remote AppServer that is the remote host's sign-in. A model-service connection never exposes a token, so it always uses the local route.

## 5. Managed model and runtime

One immutable model descriptor:

| Property | Value |
|----------|-------|
| Product id | `whisper-base-multilingual-v1` |
| File | `ggml-base.bin` (Whisper `base`, multilingual, MIT) |
| Source | `ggerganov/whisper.cpp` on Hugging Face, pinned revision `80da2d8bfee42b0e836fc3a9890373e5defc00a6` |
| SHA-256 | `60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe` |
| Expected bytes | `147951465` |
| Runtime | Default CPU whisper.cpp binding for the current platform and architecture |

The download URL is derived from the pinned revision, never a moving branch. Main verifies size and SHA-256 before the model becomes available.

Managed storage lives in the user's global DotCraft cache, never in a workspace:

```text
~/.craft/cache/voice/
  models/whisper-base-multilingual-v1/ggml-base.bin
  downloads/whisper-base-multilingual-v1.part
  temp/{sessionId}.wav
```

Lifecycle rules:

- Install downloads to `.part`, resumes when byte ranges are available, verifies the hash, then atomically promotes the file.
- Explicit Cancel stops the download and deletes `.part`; a transient network failure or application exit preserves `.part` for the next Install or Retry.
- Remove requires confirmation and deletes the installed model, partial download, idle worker, and retryable audio.
- A size or hash mismatch produces `damaged`; the worker never loads that file. Repair removes it and performs a clean install.
- The model is not bundled in the installer and is never silently replaced.
- Install, repair, and remove never run concurrently.

The worker starts lazily for the first transcription, stays alive for reuse, and is terminated on orderly exit. A crash fails the active job with a stable error and permits one clean worker restart on Retry. The worker loads only the verified model path, always uses automatic language detection, and returns only a trimmed transcript or a stable error code; failures never carry transcript text, audio, or paths. Cancelling active inference terminates the worker, rejects the request as `cancelled`, and a later job creates a clean process; a cancelled session can never produce a late completion.

## 6. ChatGPT transcription

The ChatGPT route is available when all of these hold:

1. The Desktop setting `voice.chatGptTranscription` is on (default on).
2. The connected AppServer advertises `authOpenAiOAuth`.
3. `auth/openai/status` with `includeToken: true` reports `loggedIn: true` and returns `authToken` (see [OpenAI Subscription Auth](../architecture/openai-subscription-auth.md#host-access-boundary)).

Main refreshes availability, without keeping the token, when the AppServer connection changes, when `auth/openai/usage/updated` arrives, and when the setting changes. Main reads a fresh token for every request and never caches, persists, logs, or forwards it.

Request contract:

| Item | Value |
|------|-------|
| Endpoint | `POST https://chatgpt.com/backend-api/transcribe` |
| Body | `multipart/form-data` with one `file` part: the session WAV (16 kHz mono PCM16), content type `audio/wav` |
| Language | Not sent; the service detects it |
| Headers | `Authorization: Bearer <authToken>`, `ChatGPT-Account-Id: <accountId>`, and the same `originator` value DotCraft sends on ChatGPT Responses requests |
| Response | JSON; only `text` is read |
| HTTP stack | Electron `net.fetch`; other HTTP stacks receive a bot challenge on this endpoint |

Response handling:

- HTTP 401 reads a token with `refreshToken: true` and retries once. A second 401, or a status without a token, fails with `auth-required`.
- HTTP 429 fails with `usage-limit`; a connection failure fails with `network-error`; any other failure fails with `transcription-failed`.
- Discarding the session, removing its origin, or exiting Desktop aborts the request.

## 7. Desktop voice boundary

The Desktop voice boundary exposes microphone permission and recovery, model install/cancel/remove/repair, runtime snapshots, transcription submit/retry/discard, and session events. Main owns ids, queue admission, and lifecycle; the renderer cannot supply filesystem paths. Subscriptions are scoped to their owning window, and Main accepts audio-only media requests only from DotCraft windows.

- Model phases: `missing`, `downloading`, `installed`, `damaged`, `failed`.
- Unresolved session phases: `recording`, `queued`, `transcribing`, `retryable`. A session retains its origin, `insert` or `send` intent, and measured duration.
- Snapshots include model state, ChatGPT availability, and the two-session capacity.

Submission carries 16 kHz mono PCM16 and returns a session id after admission, before inference. Main validates the payload and rejects a third unresolved session with `queue-full`. The route is chosen when transcription or retry starts. Only a completed event carries transcript text.

Stable error codes: `permission-denied`, `device-missing`, `device-unavailable`, `model-missing`, `model-damaged`, `download-failed`, `queue-full`, `invalid-audio`, `worker-unavailable`, `worker-crashed`, `transcription-failed`, `network-error`, `auth-required`, `usage-limit`, `cancelled`.

## 8. Audio capture

- Capture one channel, resample to 16 kHz mono, and convert to PCM16. The same frames drive a real RMS waveform; production never substitutes timer-driven animation.
- Recording stops at five minutes, which keeps the payload bounded.
- All media tracks stop after stop, abort, permission failure, device loss, or window unmount.
- Recordings with no frames or shorter than 250 ms are discarded silently.
- If an explicitly selected device is missing or unsatisfiable, capture retries once with the system default. A successful fallback clears the stale preference and surfaces a concise Settings notice.
- A device held by another application, or an audio-graph failure after a stream is acquired, maps to `device-unavailable` and closes the partial stream; recovery tells the user to release the microphone and retry.
- Device errors are recoverable: the microphone stays retryable, and a successful Settings probe or device selection clears stale device errors across Composers.

## 9. Session and queue lifecycle

Desktop permits at most two unresolved sessions: one `transcribing`, and one more `recording`, `queued`, or `retryable`. Main runs transcription on either route in FIFO order, never two at once.

- Starting a new recording discards any existing retryable session and its audio before capture begins.
- Stopping a recording while another session is transcribing makes it `queued`.
- Retry reuses the original session id, audio, origin, intent, and capacity slot, waits in the same FIFO queue, and chooses the route again when it starts.
- When both slots are occupied, every other Composer disables its microphone and explains the busy state in a tooltip.
- Removing an originating thread discards its unresolved session and audio. Quitting Desktop cancels all work, terminates the worker, and deletes every temporary WAV.
- A late response for a discarded or cancelled session is ignored.

Successful audio is deleted once the completion is accepted for writeback. Failed audio is retained only while its session is retryable and is never exposed as history.

## 10. Workflows

### 10.1 Record and insert

1. Capture starts only when a route, permission, device, foreground, and queue state allow it.
2. Microphone toggle, shortcut release, navigation, or the five-minute limit stops with the `insert` intent; Escape aborts without submission.
3. Stopping moves the originating Composer atomically into a finalizing state before any audio work begins, so the Composer never shows an idle frame between recording and Main admission. Main admits the session and exposes `queued` or `transcribing`.
4. Success appends to the latest originating draft (§12) without sending, then the temporary audio is deleted.

### 10.2 Record and explicitly send

1. While recording and no Agent turn owns the primary action, the user may activate Send.
2. Recording stops with the `send` intent and follows the insert path.
3. Success appends to the latest draft, then invokes the normal Composer submit path. Composer validation stays authoritative; if submission cannot proceed, the merged draft remains.
4. An Agent Stop action stays independent and never changes the voice intent.

### 10.3 Navigate and complete in the background

1. Leaving an originating thread Composer while recording stops with `insert`; queued or active transcription continues in the Voice Coordinator.
2. Completion updates the originating thread's stored draft even when its editor is unmounted. Failure records retryable state only for that origin. Neither emits a cross-thread notification; returning to the origin restores its voice state.
3. Main welcome and Agent Builder welcome are transient pre-thread origins held in memory. Leaving either before completion discards the result without creating a thread or notification.

### 10.4 Fail and retry

1. A worker, network, or transcription failure preserves the session audio and turns only the originating microphone into Retry. There is no automatic route fallback.
2. A successful Retry follows the original insert or send workflow.
3. Starting a new recording, discarding the session, removing the model or origin, or exiting Desktop removes retained audio.

## 11. Composer behavior

### 11.1 Placement and states

- Thread, main welcome, and Agent Builder welcome Composers share one voice control and state vocabulary, built from design-system primitives per the [Composer design rules](../architecture/DESIGN.md#composer).
- The microphone is the last secondary action, immediately before Send. Voice states never displace or repurpose the primary action (Quick Chat is the exception, §11.4).

| State | Composer behavior |
|-------|-------------------|
| No route | ChatGPT route unavailable and model missing. Microphone opens first-use setup. |
| Downloading | Model downloading and ChatGPT unavailable. The microphone slot shows only a compact progress ring. |
| Idle | Microphone enabled when device and queue state allow. |
| Recording | Elapsed time, live waveform, and inline Stop. |
| Finalizing, queued, transcribing | Frozen duration and a quiet disabled control, without spinner or status text. |
| Retryable | Microphone becomes Retry; no inline error row. `usage-limit`, `auth-required`, and `network-error` tooltips name the recovery. |
| Queue full | Microphone disabled with a tooltip explaining another voice input is processing. |
| Permission denied | The next microphone action opens permission recovery. |
| Device missing | Microphone stays available for retry. |

While the owning Composer records, finalizes, or transcribes, its footer is compact: it keeps the `+` action, the voice state, and Send, and hides approval, mode, goal, model, reasoning, and context-usage controls. Downloading and every other state keep the normal controls. Controls below the Composer (workspace, location, branch, usage) stay visible.

### 11.2 Input controls

- Clicking an idle microphone starts recording; clicking again stops, transcribes, and inserts.
- Holding `Ctrl+Shift+D` while DotCraft is foreground starts recording; releasing stops and inserts. The shortcut is fixed and is not an OS global shortcut. A short pointer hold also acts as hold-to-dictate.
- Escape aborts recording and leaves the draft unchanged.
- With no Agent turn active, Send during recording stops with `send`. With a turn active, its Stop action controls only the Agent.

### 11.3 Navigation

Navigation follows §10.3. The originating draft keeps its queued, transcribing, or retry state, and navigation remains available throughout.

### 11.4 Desktop pet Quick Chat

Quick Chat has one circular control and no separate microphone. It dictates through its source Composer: recording, transcription, and writeback run there under the source origin, and the transcript reaches Quick Chat through the shared draft.

- With an empty draft and no turn to stop, the control is the microphone while the source origin can record (a route exists, permission is not blocked, and no other origin is recording, finalizing, or filling the queue). Otherwise it is a disabled Send. Quick Chat never opens first-use setup or permission recovery; those stay in the main window.
- Clicking starts recording and clicking again stops with `insert`. There is no hold gesture or shortcut. Escape aborts.
- While recording the control is a pressed Stop; while finalizing or transcribing it is the quiet disabled control and Enter does not send. A retryable session with an empty draft makes it Retry.
- Once a draft exists and nothing is recording or transcribing, the control returns to Send, or to the configured follow-up action while a turn runs.
- Quick Chat stays open while its source is recording or transcribing.

## 12. Draft insertion

A transcript applies to the latest draft, not the draft or caret captured when recording started:

1. Trim the transcript; an empty result changes nothing.
2. If the draft is empty or ends in whitespace, append directly; otherwise append one space, then the transcript.

The append operates on structured Composer content: existing file, command, skill, image, and attachment data is unchanged; the transcript becomes or extends the trailing text segment and is never reinterpreted as a mention. A mounted Composer updates immediately and moves the selection to the end; an unmounted one updates the stored draft. The Composer text-length limit stays authoritative.

For `send`, Voice Input never bypasses turn-busy, attachment, permission, or validation behavior owned by the Composer. If the originating thread no longer exists, the transcript and audio are discarded without creating a thread or notification.

## 13. Settings and first use

Voice is a Personal settings destination with two areas:

1. **Device**: a microphone selector with the system default and available input devices. The choice is a Desktop application setting and applies without an AppServer restart. A missing saved device falls back to the default with a concise notice.
2. **Models**: a ChatGPT row with the `voice.chatGptTranscription` switch first, then the managed Whisper model.

- The ChatGPT row states that recordings are sent to ChatGPT. Without a ChatGPT sign-in the switch is disabled and the row asks the user to sign in.
- The model row offers Install, Remove (with confirmation explaining local Voice Input is unavailable until reinstalled), Cancel with detailed progress while downloading, Retry after download failure, and Repair when damaged.
- Device names are shown in full on hover. Presentation hides OS-appended hardware suffixes; capture uses the unchanged device id.

The page has no input meter, shortcut editor, history, dictionary, or model picker.

First use from a Composer: clicking the microphone with the model missing and no ChatGPT route opens a compact setup dialog naming OpenAI Whisper (MIT). Confirming starts the model download in the background and does not touch microphone permission. The Composer then shows only the progress ring; Settings shows detailed progress and recovery. Closing or completing the dialog restores the underlying surface.

Microphone authorization is just in time and independent of model installation:

1. With a route available, the first microphone action queries the OS permission state.
2. `denied` or `restricted` opens permission recovery immediately; any other state requests permission and acquires the microphone in the same user action.
3. Success starts recording. A denial at the native prompt returns to idle without another dialog; the next action opens recovery.
4. Recovery opens the OS microphone settings. Returning to DotCraft refreshes status but never starts recording.
5. Opening the Settings device selector may briefly acquire permission to reveal device labels; a denial keeps the menu closed and shows inline recovery.
6. On Windows, acquiring the microphone is the authoritative access check; on macOS, the OS media-access status and native request are also used.

## 14. Failure behavior

Failures preserve the latest draft. Permission and device recovery follow §8 and §13, model recovery §5, ChatGPT errors §6. Transcription failures retain audio only for explicit retry. Empty transcripts succeed without changing the draft. Deleted origins and shutdown discard unresolved audio and suppress late results.

## 15. Privacy and security

- Voice Input makes two network transfers: the pinned model download, and the session audio upload to ChatGPT while that route is active. ChatGPT may retain uploaded audio under its own policy.
- Recorded audio and transcript text never go to telemetry or logs. Logs use session ids and stable error codes; paths and raw provider exceptions are sensitive diagnostics.
- Main accepts only the fixed model descriptor and generated session paths. Model validation occurs after every download and before worker initialization.

## 16. Platform support

Windows x64/arm64 and macOS x64/arm64 are supported; Linux is out of scope. Each package carries exactly one matching CPU whisper.cpp binding, runs the worker under the packaged application security policy (including macOS signing and notarization), and must complete a real multilingual transcription. GPU runtimes are not packaged or downloaded. Third-party notices cover the binding, whisper.cpp, and the model source.
