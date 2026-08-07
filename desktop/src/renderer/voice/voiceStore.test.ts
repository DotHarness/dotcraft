import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { VoiceRuntimeSnapshot, VoiceSessionEvent } from '../../shared/voice'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import { useThreadStore } from '../stores/threadStore'

const { captureStart } = vi.hoisted(() => ({ captureStart: vi.fn() }))

vi.mock('./audioCapture', async () => {
  const actual = await vi.importActual<typeof import('./audioCapture')>('./audioCapture')
  return {
    ...actual,
    VoiceAudioCapture: { start: captureStart }
  }
})

import { shouldUseCompactVoiceFooter, useVoiceStore } from './voiceStore'
import { registerComposerVoiceTarget } from './composerDraftBridge'

const INSTALLED_SNAPSHOT: VoiceRuntimeSnapshot = {
  model: { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 },
  sessions: [],
  capacity: 2
}

describe('voiceStore recording finalization', () => {
  let snapshotListener: ((snapshot: VoiceRuntimeSnapshot) => void) | null
  let sessionListener: ((event: VoiceSessionEvent) => void) | null
  let stopCapture: ReturnType<typeof deferred<{ durationMs: number; pcm16: ArrayBuffer }>>
  let submit: ReturnType<typeof deferred<{ sessionId: string }>>
  let getSnapshot: ReturnType<typeof vi.fn>

  beforeEach(async () => {
    snapshotListener = null
    sessionListener = null
    stopCapture = deferred()
    submit = deferred()
    captureStart.mockReset()
    captureStart.mockResolvedValue({
      stop: vi.fn(() => stopCapture.promise),
      abort: vi.fn().mockResolvedValue(undefined)
    })
    getSnapshot = vi.fn().mockResolvedValue(INSTALLED_SNAPSHOT)

    Object.defineProperty(globalThis, 'window', {
      configurable: true,
      value: {
        api: {
          settings: { get: vi.fn().mockResolvedValue({ voice: {} }) },
          voice: {
            getSnapshot,
            getMicrophonePermissionStatus: vi.fn().mockResolvedValue('granted'),
            requestMicrophonePermission: vi.fn().mockResolvedValue('granted'),
            submitTranscription: vi.fn(() => submit.promise),
            discardSession: vi.fn().mockResolvedValue(undefined),
            retryTranscription: vi.fn().mockResolvedValue(undefined),
            openMicrophoneSettings: vi.fn().mockResolvedValue(undefined),
            onSnapshot: vi.fn((listener: (snapshot: VoiceRuntimeSnapshot) => void) => {
              snapshotListener = listener
              return () => {}
            }),
            onSessionEvent: vi.fn((listener: (event: VoiceSessionEvent) => void) => {
              sessionListener = listener
              return () => {}
            })
          }
        },
        addEventListener: vi.fn(),
        setInterval,
        clearInterval
      }
    })

    useVoiceStore.setState({
      initialized: false,
      snapshot: INSTALLED_SNAPSHOT,
      recording: null,
      finalizing: null,
      microphonePermission: 'granted',
      deviceFallback: false,
      localErrors: {}
    })
    useVoiceStore.getState().initialize()
    await Promise.resolve()
    await useVoiceStore.getState().startRecording('thread-1')
    useVoiceStore.setState((state) => ({
      recording: state.recording ? { ...state.recording, elapsedMs: 1_000 } : null
    }))
  })

  it('keeps compact state continuously until Main exposes the admitted session', async () => {
    const compactStates: boolean[] = []
    const unsubscribe = useVoiceStore.subscribe((state) => {
      compactStates.push(shouldUseCompactVoiceFooter(
        state.snapshot,
        state.recording?.threadId,
        state.finalizing?.threadId,
        'thread-1'
      ))
    })

    const stopping = useVoiceStore.getState().stopRecording('insert')
    expect(useVoiceStore.getState().recording).toBeNull()
    expect(useVoiceStore.getState().finalizing).toMatchObject({
      threadId: 'thread-1',
      intent: 'insert',
      durationMs: 1_000
    })

    stopCapture.resolve({ durationMs: 1_200, pcm16: new ArrayBuffer(8) })
    await vi.waitFor(() => expect(useVoiceStore.getState().finalizing?.durationMs).toBe(1_200))

    const queued: VoiceRuntimeSnapshot = {
      ...INSTALLED_SNAPSHOT,
      sessions: [{
        sessionId: 'voice-session',
        threadId: 'thread-1',
        intent: 'insert',
        phase: 'queued',
        durationMs: 1_200
      }]
    }
    getSnapshot.mockResolvedValue(queued)
    snapshotListener?.(queued)

    expect(useVoiceStore.getState().finalizing).toBeNull()
    expect(compactStates.length).toBeGreaterThan(0)
    expect(compactStates.every(Boolean)).toBe(true)

    submit.resolve({ sessionId: 'voice-session' })
    await stopping
    unsubscribe()
  })

  it('returns directly to idle for sub-250 ms audio', async () => {
    const stopping = useVoiceStore.getState().stopRecording('insert')
    expect(useVoiceStore.getState().finalizing).not.toBeNull()

    stopCapture.resolve({ durationMs: 249, pcm16: new ArrayBuffer(2) })
    await stopping

    expect(useVoiceStore.getState().recording).toBeNull()
    expect(useVoiceStore.getState().finalizing).toBeNull()
    expect(window.api.voice.submitTranscription).not.toHaveBeenCalled()
  })

  it('clears finalizing and exposes the existing error state when admission fails', async () => {
    const stopping = useVoiceStore.getState().stopRecording('insert')
    stopCapture.resolve({ durationMs: 1_000, pcm16: new ArrayBuffer(8) })
    await vi.waitFor(() => expect(window.api.voice.submitTranscription).toHaveBeenCalled())
    submit.reject(new Error('voice:queue-full'))

    await stopping

    expect(useVoiceStore.getState().finalizing).toBeNull()
    expect(useVoiceStore.getState().localErrors['thread-1']).toBe('queue-full')
  })

  it('preserves explicit send after the originating Composer unmounts', async () => {
    const submitDraft = vi.fn(async () => {})
    useComposerDraftStore.getState().saveDraft('thread-1', {
      text: 'Existing',
      segments: [{ type: 'text', value: 'Existing' }],
      images: [],
      files: []
    })
    useThreadStore.getState().setThreadList([{
      id: 'thread-1',
      displayName: 'Thread 1',
      status: 'active',
      originChannel: 'test',
      createdAt: '2026-01-01T00:00:00Z',
      lastActiveAt: '2026-01-01T00:00:00Z'
    }])
    const unregister = registerComposerVoiceTarget('thread-1', {
      capture: () => useComposerDraftStore.getState().getDraft('thread-1')!,
      apply: () => {},
      submit: submitDraft
    })

    const stopping = useVoiceStore.getState().stopRecording('send')
    unregister()
    stopCapture.resolve({ durationMs: 1_000, pcm16: new ArrayBuffer(8) })
    submit.resolve({ sessionId: 'send-session' })
    await stopping
    sessionListener?.({
      sessionId: 'send-session',
      threadId: 'thread-1',
      intent: 'send',
      phase: 'transcribing',
      durationMs: 1_000,
      type: 'completed',
      transcript: 'spoken'
    })

    await vi.waitFor(() => expect(submitDraft).toHaveBeenCalledWith(
      expect.objectContaining({ text: 'Existing spoken' })
    ))
    useComposerDraftStore.getState().clearDraft('thread-1')
  })

  it('discards an admitted session when its origin is removed during snapshot refresh', async () => {
    const lateSnapshot = deferred<VoiceRuntimeSnapshot>()
    getSnapshot.mockReturnValueOnce(lateSnapshot.promise)
    const stopping = useVoiceStore.getState().stopRecording('insert')
    stopCapture.resolve({ durationMs: 1_000, pcm16: new ArrayBuffer(8) })
    await vi.waitFor(() => expect(window.api.voice.submitTranscription).toHaveBeenCalled())
    submit.resolve({ sessionId: 'late-session' })
    await vi.waitFor(() => expect(getSnapshot).toHaveBeenCalledTimes(2))

    await useVoiceStore.getState().discardOrigin('thread-1')
    lateSnapshot.resolve({
      ...INSTALLED_SNAPSHOT,
      sessions: [{
        sessionId: 'late-session',
        threadId: 'thread-1',
        intent: 'insert',
        phase: 'queued',
        durationMs: 1_000
      }]
    })
    await stopping

    expect(window.api.voice.discardSession).toHaveBeenCalledWith('late-session')
    expect(useVoiceStore.getState().snapshot.sessions).toEqual([])
    expect(useVoiceStore.getState().finalizing).toBeNull()
  })
})

describe('voiceStore capture admission and origin cleanup', () => {
  beforeEach(() => {
    captureStart.mockReset()
    Object.defineProperty(globalThis, 'window', {
      configurable: true,
      value: {
        api: {
          settings: {
            get: vi.fn().mockResolvedValue({ voice: {} }),
            set: vi.fn().mockResolvedValue(undefined)
          },
          voice: {
            getSnapshot: vi.fn().mockResolvedValue(INSTALLED_SNAPSHOT),
            getMicrophonePermissionStatus: vi.fn().mockResolvedValue('granted'),
            requestMicrophonePermission: vi.fn().mockResolvedValue('granted'),
            submitTranscription: vi.fn().mockResolvedValue({ sessionId: 'session' }),
            discardSession: vi.fn().mockResolvedValue(undefined),
            retryTranscription: vi.fn().mockResolvedValue(undefined),
            openMicrophoneSettings: vi.fn().mockResolvedValue(undefined),
            onSnapshot: vi.fn(() => () => {}),
            onSessionEvent: vi.fn(() => () => {})
          }
        },
        addEventListener: vi.fn(),
        setInterval,
        clearInterval
      }
    })
    useVoiceStore.setState({
      initialized: false,
      snapshot: INSTALLED_SNAPSHOT,
      recording: null,
      finalizing: null,
      microphonePermission: 'granted',
      deviceFallback: false,
      localErrors: {}
    })
    useVoiceStore.getState().initialize()
  })

  it('reserves startup and aborts a capture that resolves after cancellation', async () => {
    const pendingCapture = deferred<{
      stop: ReturnType<typeof vi.fn>
      abort: ReturnType<typeof vi.fn>
    }>()
    const abort = vi.fn().mockResolvedValue(undefined)
    captureStart.mockReturnValue(pendingCapture.promise)

    const first = useVoiceStore.getState().startRecording('thread-start')
    await vi.waitFor(() => expect(captureStart).toHaveBeenCalledTimes(1))
    await useVoiceStore.getState().startRecording('thread-start')
    expect(captureStart).toHaveBeenCalledTimes(1)

    useVoiceStore.getState().cancelRecordingStart('thread-start')
    pendingCapture.resolve({ stop: vi.fn(), abort })
    await first

    expect(abort).toHaveBeenCalledTimes(1)
    expect(useVoiceStore.getState().recording).toBeNull()
  })

  it('discards every Main session owned by a removed origin', async () => {
    useVoiceStore.setState({
      snapshot: {
        ...INSTALLED_SNAPSHOT,
        sessions: [
          { sessionId: 'queued', threadId: 'removed', intent: 'insert', phase: 'queued', durationMs: 500 },
          { sessionId: 'other', threadId: 'other', intent: 'insert', phase: 'transcribing', durationMs: 500 }
        ]
      },
      finalizing: { threadId: 'removed', intent: 'insert', durationMs: 500 },
      localErrors: { removed: 'transcription-failed' }
    })

    await useVoiceStore.getState().discardOrigin('removed')

    expect(window.api.voice.discardSession).toHaveBeenCalledTimes(1)
    expect(window.api.voice.discardSession).toHaveBeenCalledWith('queued')
    expect(useVoiceStore.getState().finalizing).toBeNull()
    expect(useVoiceStore.getState().localErrors.removed).toBeUndefined()
  })
})

function deferred<T>(): {
  promise: Promise<T>
  resolve(value: T): void
  reject(reason: unknown): void
} {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}
