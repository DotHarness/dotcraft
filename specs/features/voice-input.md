# DotCraft Desktop Voice Input Specification

| Field | Value |
|-------|-------|
| **Version** | 1.3.0 |
| **Status** | Living |
| **Date** | 2026-08-07 |
| **Related Specs** | [Desktop Client](../clients/desktop-client.md), [Design System](../architecture/DESIGN.md) |

Purpose: define local speech-to-text input for the DotCraft Desktop Composer.

---

## 1. Scope

Voice Input lets a user record speech in a DotCraft Composer, transcribe it with a DotCraft-managed local Whisper model, and append the transcript to the originating draft.

The feature includes:

- Foreground-only recording from thread, main welcome, and Agent Builder welcome Composers.
- Click-to-toggle and fixed hold-to-dictate interaction.
- A real signal-driven recording waveform.
- Local transcription through an isolated TypeScript inference worker using whisper.cpp Node bindings.
- One managed multilingual model with automatic language detection.
- Model installation, validation, repair, cancellation, and removal.
- Microphone selection and permission recovery in Desktop settings.
- Background transcription and writeback after thread navigation.
- A bounded local transcription queue and true retry after failure.

The feature is owned entirely by Desktop. Audio, model state, and transcription jobs do not cross the AppServer boundary.

## 2. Principles

- **Local by default.** Recorded audio is processed on the device and is never sent to AppServer or another speech service.
- **Composer-native.** Voice Input adds text only to a DotCraft Composer. It does not inject text into other applications.
- **Draft-safe.** Recording, transcription, navigation, and failure must not destroy or replace existing draft content.
- **Quiet feedback.** Composer state uses existing inline controls without redundant status copy, percentages, or success notifications.
- **One managed path.** V1 exposes no provider, language, or model selection.
- **Bounded local work.** One worker serializes inference, and Desktop accepts at most two unresolved voice sessions.
- **Agent independence.** Voice Input does not change Agent execution, stop, queue, or send semantics.
- **Design-system owned.** Voice Input reuses maintained Composer, settings, dialog, tooltip, select, progress, and action primitives rather than introducing a parallel visual language.
- **User-facing language.** Product copy describes the action or recovery available to the user and does not expose worker, PCM, provider, model-taxonomy, or queue terminology.

## 3. Non-goals

V1 does not include:

- Voice chat, text-to-speech, or live conversational audio.
- Cloud speech providers, credentials, or provider selection.
- AppServer, SDK, CLI, Channel, or protocol support for speech-to-text.
- Linux release support.
- Custom model import, model selection, or model update controls.
- Manual recognition-language selection.
- Global background dictation or text injection into another application.
- Customizable shortcuts or shortcut settings.
- Streaming partial transcripts.
- Dictation dictionaries, recording history, or transcript history.
- Input-level testing in Voice settings.
- Retaining successful recordings.

## 4. Architecture boundary

Voice Input uses four Desktop-owned layers:

| Layer | Responsibility |
|-------|----------------|
| Renderer capture | Requests the selected microphone after Main reports the operating-system permission state, captures mono PCM with an AudioWorklet, resamples it, and feeds the waveform and root Voice Coordinator. |
| Renderer Voice Coordinator | Owns recording and session UI state across route changes and writes completed transcripts to the originating draft. |
| Electron Main voice service | Owns microphone permission status and recovery, model lifecycle, bounded queue admission, temporary audio, worker lifecycle, retry state, and typed preload events. |
| TypeScript inference worker | Runs in an Electron Utility Process, loads the managed ggml model through a typed whisper.cpp binding, and performs one Whisper transcription at a time. |

Electron Main is the trust boundary:

- Renderer never receives model or temporary-audio filesystem paths.
- The inference worker never accesses thread state or Desktop settings.
- AppServer does not receive audio, transcripts, progress, or model state.
- Speech-provider types remain behind a DotCraft-owned worker adapter and do not appear in Desktop IPC.
- Remote AppServer mode does not change Voice Input because capture and inference stay local to Desktop.

The Voice Coordinator lives above the thread route. Unmounting a Composer must not destroy a recording result or prevent writeback to its originating draft.

## 5. Managed model and runtime

V1 uses one immutable model descriptor:

| Property | Value |
|----------|-------|
| Product id | `whisper-base-multilingual-v1` |
| File | `ggml-base.bin` |
| Model | Whisper `base`, multilingual |
| User-facing name | `Whisper Multilingual` |
| License | MIT |
| Source repository | `ggerganov/whisper.cpp` on Hugging Face |
| Pinned revision | `80da2d8bfee42b0e836fc3a9890373e5defc00a6` |
| SHA-256 | `60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe` |
| Expected bytes | `147951465` |
| Product display size | 148 MB |
| Worker dependency | `@fugood/whisper.node` `1.1.1` |
| Baseline runtime | Default CPU prebuilt for the current platform and architecture |

The model download URL is derived from the pinned revision, never a moving branch. Main verifies SHA-256 before making the model available. GPU variants are future optimizations and are not packaged in V1.

Managed storage is rooted under the user's global DotCraft cache at `~/.craft/cache/voice`:

```text
~/.craft/cache/voice/
  models/whisper-base-multilingual-v1/ggml-base.bin
  downloads/whisper-base-multilingual-v1.part
  temp/{sessionId}.wav
```

Lifecycle rules:

- Install downloads to `.part`, resumes when byte ranges are available, verifies the hash, then atomically promotes the file.
- Explicit Cancel stops the request and deletes `.part`.
- A transient network failure or application exit preserves `.part` for the next Install or Retry.
- Remove requires confirmation and deletes the installed model, partial download, idle worker, and retryable audio.
- Repair removes a damaged model and performs a clean install.
- A size or hash mismatch produces `damaged`; the worker never loads that file.
- The model is not bundled in the installer and is not silently replaced by a newer model.
- The model manager prevents concurrent install, repair, and remove operations.

The TypeScript worker entry and default whisper.cpp native binding are packaged with Desktop for Windows x64/arm64 and macOS x64/arm64. The Electron Utility Process starts lazily for the first transcription, stays alive for reuse, and is terminated during orderly application exit. A crash fails the active job with a stable error and permits one clean worker restart on Retry.

The pinned `@fugood/whisper.node` package does not publish the declaration file referenced by its manifest. DotCraft therefore owns a narrow ambient declaration for only the private worker APIs it uses. Any binding upgrade must revalidate the published package shape, remove or update that declaration as appropriate, and repeat packaged native-module smoke tests.

## 6. Desktop voice boundary

Preload exposes a typed `window.api.voice` namespace. This is a Desktop host API, not an AppServer or SDK contract.

```ts
type VoiceModelPhase =
  | 'missing'
  | 'downloading'
  | 'installed'
  | 'damaged'
  | 'failed'

type VoiceSessionPhase =
  | 'recording'
  | 'queued'
  | 'transcribing'
  | 'retryable'

type VoiceIntent = 'insert' | 'send'

type VoiceErrorCode =
  | 'permission-denied'
  | 'device-missing'
  | 'device-unavailable'
  | 'model-missing'
  | 'model-damaged'
  | 'download-failed'
  | 'queue-full'
  | 'invalid-audio'
  | 'worker-unavailable'
  | 'worker-crashed'
  | 'transcription-failed'
  | 'cancelled'

interface VoiceModelState {
  phase: VoiceModelPhase
  bytesDownloaded: number
  bytesTotal: number | null
  errorCode?: VoiceErrorCode
}

interface VoiceSessionState {
  sessionId: string
  threadId: string
  intent: VoiceIntent
  phase: VoiceSessionPhase
  durationMs: number
  errorCode?: VoiceErrorCode
}

interface VoiceRuntimeSnapshot {
  model: VoiceModelState
  sessions: VoiceSessionState[]
  capacity: 2
}

type VoiceMicrophonePermissionStatus =
  | 'not-determined'
  | 'granted'
  | 'denied'
  | 'restricted'
  | 'unknown'

interface VoiceTranscriptionInput {
  threadId: string
  intent: VoiceIntent
  durationMs: number
  pcm16: ArrayBuffer
}

interface VoiceSessionEvent extends VoiceSessionState {
  type: 'changed' | 'completed' | 'discarded'
  transcript?: string
}

interface VoiceApi {
  getMicrophonePermissionStatus(): Promise<VoiceMicrophonePermissionStatus>
  requestMicrophonePermission(): Promise<VoiceMicrophonePermissionStatus>
  openMicrophoneSettings(): Promise<void>
  getSnapshot(): Promise<VoiceRuntimeSnapshot>
  installModel(): Promise<void>
  cancelModelInstall(): Promise<void>
  removeModel(): Promise<void>
  repairModel(): Promise<void>
  submitTranscription(input: VoiceTranscriptionInput): Promise<{ sessionId: string }>
  retryTranscription(sessionId: string): Promise<void>
  discardSession(sessionId: string): Promise<void>
  onSnapshot(listener: (snapshot: VoiceRuntimeSnapshot) => void): () => void
  onSessionEvent(listener: (event: VoiceSessionEvent) => void): () => void
}
```

Boundary invariants:

- Main validates every payload, duration, thread id, intent, and queue transition.
- `pcm16` contains mono signed 16-bit little-endian samples at 16 kHz.
- `submitTranscription` creates a Main-owned WAV and returns after queue admission, not after inference.
- Only a completed event may contain transcript text.
- Errors expose codes and safe English fallbacks, not raw worker exceptions.
- Event subscriptions return an unsubscribe function and are removed with the owning window.
- Main rejects a third unresolved session with `queue-full`, even if Renderer state is stale.
- Microphone selection remains in the existing Desktop settings API.
- The operating system is the only source of truth for permission state; Desktop settings never persist an application-owned granted flag.
- Main permits pure-audio media requests only from a DotCraft application window. Video, mixed audio/video, and unrelated WebContents requests are denied.
- The existing sanitized clipboard-write permission for DotCraft application windows remains available.

## 7. Worker boundary

Main and the TypeScript inference worker communicate through Electron Utility Process structured-clone messages. This boundary is private to Desktop and carries request ids, stable methods, and structured results only.

| Method | Required data | Result |
|--------|---------------|--------|
| `initialize` | Protocol version and verified model path | Runtime and model are ready. |
| `transcribe` | Request id, session id, and Main-owned WAV path | Trimmed transcript and detected-language metadata. |

Responses correlate to request ids. Failures contain a stable worker error code and safe message. They do not contain transcript text, audio bytes, or model/audio paths. Main maps worker failures to `VoiceErrorCode`. The worker loads only the verified local model path and always invokes whisper.cpp with `language: "auto"`.

The worker processes one transcription at a time. Cancelling active native inference terminates the Utility Process, rejects the active request as `cancelled`, and lazily creates a clean process for later work; a cancelled session can never produce a late completion event. Orderly Desktop shutdown also terminates the private process after rejecting pending requests.

## 8. Audio capture and waveform

Renderer uses `getUserMedia` with the selected-device constraint and captures raw frames with an AudioWorklet. It does not rely on MediaRecorder WebM/Opus or add FFmpeg in V1.

Capture rules:

- Request or mix one channel, then resample to 16 kHz mono.
- Convert samples to PCM16 for transcription.
- Use the same frames to compute a real RMS-driven waveform.
- Batch worklet messages to avoid one IPC or React update per render quantum.
- Keep the complete five-minute PCM payload below approximately 10 MB.
- Stop all MediaStream tracks after stop, abort, permission failure, device loss, or Desktop-window unmount.
- Discard recordings with no frames or measured duration below 250 ms.
- If an explicitly selected device fails with `NotFoundError` or `OverconstrainedError`, retry once with the system default device. A successful fallback clears the stale device preference and exposes a concise Settings notice.
- `NotReadableError` and `TrackStartError` map to `device-unavailable`; recovery tells the user to release the microphone from another application and retry.
- Audio-graph initialization failures after a stream is acquired map to `device-unavailable`, never `device-missing`, and close the acquired stream and partial graph.
- A device error is recoverable: the Composer microphone remains retryable, and a successful Settings probe or device selection clears stale device errors across Composers.

The inline waveform is a canvas backed by real samples. Deterministic PCM fixtures may drive design-system and unit-test previews; production does not substitute timer-only CSS bars.

## 9. Session and queue lifecycle

Desktop permits at most two unresolved sessions:

1. One session may be `transcribing`.
2. One additional session may be `recording`, `queued`, or `retryable`.

The worker executes transcription in first-in, first-out order and never runs two inferences concurrently.

Additional rules:

- Starting a new recording discards any existing retryable session and deletes its temporary audio before capture begins.
- Stopping the second recording while another session is transcribing changes it to `queued`.
- Retry reuses the original session id, audio, thread id, intent, and capacity slot.
- Retry requested while the other session is active waits in the same FIFO queue.
- When both slots are occupied, every other Composer disables its microphone and explains the busy state in a tooltip.
- Removing an originating thread discards its unresolved session and audio.
- Quitting Desktop cancels all work, terminates the worker, and deletes every temporary WAV.
- A late response for a discarded or cancelled session is ignored.

Successful audio is deleted after the completion event is accepted for writeback. Failed audio is retained only while its session is retryable and is never exposed as history.

## 10. End-to-end workflows

### 10.1 Record and insert

1. Renderer admits capture only when the model, permission, device, foreground, and queue states allow recording.
2. The AudioWorklet feeds the bounded PCM buffer and real waveform from the same microphone frames.
3. Microphone toggle, shortcut release, navigation, or the five-minute limit stops with the `insert` intent; Escape aborts without submission.
4. Renderer silently discards an empty or sub-250 ms recording. Otherwise Main atomically admits it, creates the temporary WAV, and exposes `queued` or `transcribing` state.
5. The worker processes the session in FIFO order and returns either a trimmed transcript or a structured failure.
6. Success appends to the latest originating draft and never sends it. Desktop then removes the temporary audio.

### 10.2 Record and explicitly send

1. While recording and no Agent turn owns the primary action, the user may activate the normal Send action.
2. Renderer stops recording with the `send` intent and follows the same admission and transcription path as insert.
3. Success first appends to the latest draft, then invokes the normal Composer submit path.
4. Existing Composer validation remains authoritative. If submission cannot proceed, the merged draft remains recoverable.
5. An Agent Stop action remains independent and never changes the Voice Input intent.

### 10.3 Navigate and complete in the background

1. Leaving an originating thread Composer during recording stops it with `insert`; queued or active transcription continues in the root Voice Coordinator.
2. Completion updates the originating thread's latest stored draft even when its editor is unmounted. Failure records retryable state only for that origin.
3. Neither success nor failure emits a cross-thread Toast. Returning to the origin restores its current voice state.
4. Main welcome and Agent Builder welcome are transient pre-thread origins. Leaving either before completion discards the result without creating a thread or notification.

### 10.4 Fail and retry

1. A worker or transcription failure preserves the session WAV and changes only the originating microphone position to Retry.
2. Retry keeps the original session id, origin, intent, audio, and capacity slot. It waits in FIFO order when another session is active.
3. A successful Retry follows the original insert or send workflow.
4. Starting a new recording, explicitly discarding the session, removing the model or origin, or exiting Desktop removes the retained retry audio.
5. Late completion from a cancelled or discarded worker is ignored.

## 11. Composer behavior

### 11.1 Design-system contract

- Thread, main welcome, and Agent Builder welcome Composers use the same maintained Voice Input control and state vocabulary.
- The microphone remains the final secondary action immediately before Send. Recording, download, queue, transcription, and retry states never displace or repurpose the primary action.
- Downloading, queued, transcribing, retryable, and capacity-full states communicate through the smallest existing inline control and tooltip that explains the next available action.
- Settings and first-use flows reuse production dialogs and actions. Voice Input does not introduce feature-specific modal framing, legends, or status badges.
- Design-system scenarios cover every state and workflow defined by this specification at maintained themes and viewport widths before a new visual treatment reaches production.
- Design previews may use deterministic speech-shaped PCM; production waveform motion always comes from captured microphone frames.

### 11.2 Availability states

| State | Composer behavior |
|-------|-------------------|
| Model missing | Microphone opens first-use setup. |
| Downloading | Microphone slot shows only a compact progress ring. |
| Idle | Microphone is enabled when device and queue state allow it. |
| Recording | Composer shows elapsed time, a real waveform, and inline stop. |
| Queued or transcribing | Composer uses a quiet disabled-square state without spinner or status text. |
| Retryable | Microphone becomes a retry icon with `Retry voice input`; no inline error row appears. |
| Queue full | Microphone is disabled with a tooltip explaining that another voice input is being processed. |
| Permission denied | The next microphone action opens permission recovery. |
| Device missing | Microphone remains available for retry; a successful device probe or selection clears the stale error. |

The DotCraft mascot may reflect recording only through an approved design-system state.

Recording, queued, and transcribing voice sessions use the originating Composer's compact internal footer. The leading side keeps the command/attachment `+` action while approval, mode, goal, model, reasoning, and context-usage controls are hidden. During recording, the live signal consumes the remaining internal width and the footer retains elapsed time, inline Stop, and the independent primary Send action. During queued transcription and transcription, the live signal is removed while the recorded duration, quiet disabled-square voice control, and disabled Send action remain visible. Model downloading and every other non-processing state retain the normal Composer controls; completion, cancellation, and retryable failure restore them. The compact state applies only to the Composer that owns the voice session. Controls below the Composer, including workspace, location, branch, and usage context, remain visible throughout.

### 11.3 Input controls

- The microphone is the last secondary action in every supported Composer: it sits immediately to the left of Send, after model and context controls.
- Clicking an idle microphone starts recording; clicking again stops, transcribes, and inserts.
- Holding `Ctrl+Shift+D` while DotCraft is foreground starts recording; releasing stops, transcribes, and inserts.
- The shortcut is fixed and is not an operating-system global shortcut.
- Pointer interaction uses a 150 ms threshold to distinguish click from hold.
- Escape aborts recording and leaves the draft unchanged.
- At five minutes, Desktop stops and uses the `insert` intent.
- When no Agent turn is active, normal Send during recording stops with the `send` intent.
- When an Agent turn is active, its Stop action remains the Agent Stop action and does not control Voice Input.
- Main welcome and Agent Builder welcome use in-memory virtual draft origins until their first thread exists. They support the same insert and explicit-send intents without creating a thread before transcription completes.

### 11.4 Navigation and background completion

For an originating thread Composer, changing threads, opening Settings, or otherwise unmounting it stops active recording with `insert`. Transcription continues through the root Voice Coordinator.

- Success silently appends to the originating thread draft.
- Failure silently changes that originating session to retryable.
- Neither outcome shows a Toast in another thread.
- Returning to the origin restores queued, transcribing, or retry state.
- Navigation remains available during recording, queueing, and transcription.
- Main welcome and Agent Builder welcome are pre-thread, transient origins. Leaving either surface before transcription completes discards that result without creating a thread or showing a Toast.

## 12. Draft insertion and submission

A transcript applies to the latest draft, not the draft or caret captured when recording started.

Append behavior:

1. Trim the transcript.
2. Do nothing when the trimmed transcript is empty.
3. If the latest draft is empty or ends in whitespace, append directly.
4. Otherwise append one ASCII space, then the transcript.

DotCraft applies this operation to structured Composer segments:

- Existing file, command, skill, image, and attachment data remains unchanged.
- Transcript text becomes a trailing text segment or extends the existing trailing text segment.
- Transcript text is not reinterpreted as a file, command, skill, or mention.
- A mounted Composer updates immediately and moves selection to the new end.
- An unmounted Composer updates the originating thread draft store.
- The existing Composer text-length contract remains authoritative.

For `send`, Desktop appends to the latest draft first and invokes the normal Composer submit path. If submission fails, the merged draft remains recoverable. Voice Input does not bypass turn-busy, attachment, permission, or validation behavior owned by the Composer.

If the originating thread no longer exists, Desktop discards the transcript and deletes audio without creating another thread or notification.

## 13. Settings and first use

Voice is a Personal settings destination with exactly two product areas:

1. **Microphone** — system default and available explicit input devices.
2. **Models** — speech-to-text model installation and lifecycle. v1 contains the single managed Whisper model, while the group remains provider-neutral for future models.

Settings rules:

- An uninstalled model row shows `Install`.
- An installed model row shows `Remove` and no Ready badge.
- Downloading shows detailed progress and `Cancel` in Settings only.
- Download failure shows `Retry`; damaged validation shows `Repair`.
- Remove uses the existing production confirmation-dialog primitive.
- The confirmation explains that Voice Input is unavailable until downloaded again.
- A missing saved microphone falls back to system default and exposes a concise device notice.
- Device selection is persisted at Desktop application scope without an AppServer restart.
- Truncated device names expose the complete user-facing name in a tooltip on both the selected value and menu option. Chromium's trailing Windows USB VID:PID suffix is hidden from presentation only; capture continues to use the unchanged device ID.

The page does not contain an input meter, Dictation section, shortcut editor or explanation, recording history, dictionary, model picker, local-processing legend, or audio-deletion legend.

First use from Composer:

1. The user clicks the microphone while the model is missing.
2. DotCraft opens a compact `Set up voice input` dialog describing `OpenAI Whisper (MIT)` with `Not now` and `Set up`.
3. `Set up` closes the dialog and begins model download in the background. It does not inspect or request microphone permission.
4. Composer shows only the progress ring; Settings exposes detailed progress and recovery.

The application dialog contains no diagrams, legends, shortcut chips, model taxonomy, or developer terminology. Closing or completing it restores the underlying surface; an empty or black window is release-blocking.

Microphone authorization is just in time and independent of model installation:

1. With the model installed, the first microphone action queries the operating-system permission state.
2. `denied` or `restricted` opens permission recovery immediately. Any other state requests system permission and then calls `getUserMedia()` in the same user action.
3. Successful capture starts recording immediately. A denial from the native prompt returns the Composer to idle without stacking another dialog; the next microphone action opens recovery.
4. `Open system settings` opens the operating-system microphone settings. Returning to DotCraft refreshes status but never starts recording automatically.
5. Opening the Settings device selector may request permission, briefly acquire and release an audio stream, refresh real device labels, and then open the menu. A denial keeps the menu closed and exposes the inline recovery action.
6. Windows uses `getUserMedia()` as the authoritative access check; macOS also uses Electron's microphone media-access status and native request API.

## 14. Failure behavior

| Failure | Required behavior |
|---------|-------------------|
| Permission denied or restricted | Preserve the draft and model state. A native-prompt denial returns to idle; the next action exposes recovery and a working system-settings link. |
| Selected device missing | Fall back to system default when possible; otherwise preserve a retryable microphone action and expose recovery. |
| Device unavailable | Preserve the draft and allow retry after the user closes another application that is using the microphone. |
| Download interrupted | Preserve a resumable partial unless explicitly cancelled. |
| Hash mismatch | Mark the model damaged and offer Repair. |
| Queue full | Reject admission without writing audio and keep existing sessions unchanged. |
| Invalid or sub-250 ms audio | Discard silently and leave the draft unchanged. |
| Worker start or crash | Mark the session retryable, preserve its WAV, and restart only on Retry. |
| Transcription error | Keep the draft and expose button-level Retry in the originating Composer. |
| Empty transcript | Treat as success, delete audio, and leave the draft unchanged. |
| Submit failure after `send` | Keep the merged draft and surface the existing Composer failure. |
| Originating thread removed | Discard session, result, and audio silently. |
| Application exit | Cancel work, stop capture, terminate the worker, and delete temporary WAV files. |

## 15. Privacy, security, and diagnostics

- The only Voice Input network transfer is the pinned model download.
- Recorded PCM/WAV and transcript text are not sent to telemetry or written to logs.
- Logs use session ids and stable error codes; filesystem paths and raw provider exceptions remain sensitive diagnostics.
- Temporary audio uses the DotCraft-owned global cache under `~/.craft/cache/voice` and is never placed in a workspace.
- Renderer cannot request arbitrary model or audio paths.
- Main accepts only the fixed model descriptor and generated session paths.
- Model validation occurs before worker initialization and after every completed download.

## 16. Compatibility and release gates

| Platform | Architecture | V1 status |
|----------|--------------|-----------|
| Windows | x64 | Required |
| Windows | arm64 | Required |
| macOS | x64 | Required |
| macOS | arm64 | Required |
| Linux | Any | Out of scope |

Each required package includes the worker entry and exactly one matching default CPU native binding, starts the Utility Process under packaged application security policy, and completes a real multilingual transcription. CUDA, Vulkan, WASM, and non-target architecture packages are excluded. macOS signing, hardened runtime, entitlements, and notarization preserve worker execution.

GPU-specific runtimes are not selected, packaged, or downloaded in V1.

Release evidence must additionally prove:

- Each target installs and transcribes real multilingual audio without a development runtime.
- Windows and macOS permission denial and recovery, saved-device loss, system-default fallback, and microphone hot-plug work on real hardware.
- Download resume, explicit Cancel, checksum failure, Repair, Remove, offline use, worker crash, cancellation, queue saturation, navigation, retry replacement, and application exit reach their defined cleanup states.
- Five-minute capture size, transcription latency, cancellation latency, peak memory, and concurrent Agent-load behavior are recorded with hardware, architecture, operating-system, audio fixture, and duration context. These measurements are regression evidence, not hidden product promises.
- Logs, telemetry, workspace state, global Voice Input cache, and AppServer traffic contain no retained audio or transcript content outside the lifecycle defined here.
- Third-party notices cover `@fugood/whisper.node`, whisper.cpp, the managed model source, and packaged native dependencies.
- Packaged UI is reviewed in every supported Desktop locale, while user documentation follows the repository's English and Chinese documentation structure.

## 17. Acceptance checklist

- [ ] Voice Input operates without a cloud STT credential, AppServer, or network access after model installation.
- [ ] The fixed model downloads from its pinned revision, passes SHA-256 validation, and is loaded only by the Desktop inference worker.
- [ ] Windows x64/arm64 and macOS x64/arm64 packages include functional worker and native assets.
- [ ] The waveform responds to captured PCM rather than timer-only animation.
- [ ] Click, foreground hold, Escape, insert, explicit send, Agent Stop, navigation, and five-minute timeout follow this contract.
- [ ] Thread, main welcome, and Agent Builder welcome Composers expose the microphone immediately to the left of Send.
- [ ] A third unresolved session is rejected while one active and one queued/retryable session remain valid.
- [ ] Background success and failure are silent and restore correct origin state.
- [ ] Retry uses original audio; new recording, discard, success, removal, and exit delete it at the required time.
- [ ] Transcript append preserves the latest structured draft and uses the defined trim/space behavior.
- [ ] Settings contain only microphone and managed-model lifecycle controls and reuse production dialogs.
- [ ] Transcribing has no spinner; Composer downloading has no redundant text or percentage.
- [ ] Setup, native permission, and permission-recovery flows never leave an empty or black surface.
- [ ] New UI strings exist in `en`, `zh-Hans`, `ja`, `ko`, `es`, `fr`, and `de`.
- [ ] Audio and transcript content do not appear in logs, telemetry, AppServer traffic, or workspace files.
- [ ] Real-device and packaged-build evidence satisfies every release gate for each required platform and architecture.
- [ ] Required third-party notices and English/Chinese user documentation match the shipped behavior.

## 18. Open questions

None. Changes to product behavior, architecture ownership, queue limits, model choice, retention, supported platforms, or workflow require updating this specification before implementation.
