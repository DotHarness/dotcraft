import { create } from 'zustand'

import {
  VOICE_MAX_DURATION_MS,
  VOICE_MIN_DURATION_MS,
  type VoiceErrorCode,
  type VoiceIntent,
  type VoiceMicrophonePermissionStatus,
  type VoiceRuntimeSnapshot,
  type VoiceSessionState
} from '../../shared/voice'
import { useThreadStore } from '../stores/threadStore'
import { VoiceAudioCapture, VoiceCaptureError } from './audioCapture'
import { appendVoiceTranscript, isAvailableComposerVoiceOrigin } from './composerDraftBridge'
import { requestMicrophoneAccess } from './microphoneAccess'

interface RecordingState {
  threadId: string
  startedAt: number
  elapsedMs: number
  level: number
}

interface VoiceStoreState {
  initialized: boolean
  snapshot: VoiceRuntimeSnapshot
  recording: RecordingState | null
  microphonePermission: VoiceMicrophonePermissionStatus
  deviceFallback: boolean
  localErrors: Record<string, VoiceErrorCode | undefined>
  initialize(): void
  refreshMicrophonePermission(): Promise<VoiceMicrophonePermissionStatus>
  setMicrophonePermission(status: VoiceMicrophonePermissionStatus): void
  openMicrophoneSettings(): Promise<void>
  markDeviceFallback(): void
  clearDeviceFallback(): void
  clearDeviceErrors(): void
  startRecording(threadId: string): Promise<void>
  stopRecording(intent: VoiceIntent): Promise<void>
  abortRecording(): Promise<void>
  retry(sessionId: string): Promise<void>
  clearError(threadId: string): void
  reportError(threadId: string, code: VoiceErrorCode): void
}

const EMPTY_SNAPSHOT: VoiceRuntimeSnapshot = {
  model: { phase: 'missing', bytesDownloaded: 0, bytesTotal: null },
  sessions: [],
  capacity: 2
}

let capture: VoiceAudioCapture | null = null
let elapsedTimer: number | null = null
let focusListenerAttached = false

export const useVoiceStore = create<VoiceStoreState>((set, get) => ({
  initialized: false,
  snapshot: EMPTY_SNAPSHOT,
  recording: null,
  microphonePermission: 'unknown',
  deviceFallback: false,
  localErrors: {},

  initialize() {
    if (get().initialized) return
    if (!window.api?.voice) return
    set({ initialized: true })
    void window.api.voice.getSnapshot().then((snapshot) => set({ snapshot })).catch(() => {})
    void get().refreshMicrophonePermission()
    if (typeof window.api.voice.onSnapshot === 'function') {
      window.api.voice.onSnapshot((snapshot) => set({ snapshot }))
    }
    if (typeof window.api.voice.onSessionEvent === 'function') {
      window.api.voice.onSessionEvent((event) => {
        if (event.type !== 'completed') return
        if (!threadExists(event.threadId)) return
        void appendVoiceTranscript(event.threadId, event.transcript ?? '', event.intent === 'send')
      })
    }
    if (!focusListenerAttached) {
      focusListenerAttached = true
      window.addEventListener('focus', () => { void useVoiceStore.getState().refreshMicrophonePermission() })
    }
  },

  async refreshMicrophonePermission() {
    const status = typeof window.api.voice.getMicrophonePermissionStatus === 'function'
      ? await window.api.voice.getMicrophonePermissionStatus().catch(() => 'unknown' as const)
      : 'unknown'
    set({ microphonePermission: status })
    return status
  },

  setMicrophonePermission(status) {
    set({ microphonePermission: status })
  },

  async openMicrophoneSettings() {
    await window.api.voice.openMicrophoneSettings()
  },

  markDeviceFallback() {
    set({ deviceFallback: true })
  },

  clearDeviceFallback() {
    set({ deviceFallback: false })
  },

  clearDeviceErrors() {
    set({ localErrors: clearRecoverableDeviceErrors(get().localErrors) })
  },

  async startRecording(threadId) {
    if (capture || get().recording) return
    set({ localErrors: { ...get().localErrors, [threadId]: undefined } })
    const retryable = get().snapshot.sessions.find((session) => session.phase === 'retryable')
    if (retryable) await window.api.voice.discardSession(retryable.sessionId)
    const unresolved = get().snapshot.sessions.filter((session) => (
      session.phase !== 'recording' && session.sessionId !== retryable?.sessionId
    )).length
    if (unresolved >= get().snapshot.capacity) {
      setError(set, get, threadId, 'queue-full')
      return
    }

    try {
      const permission = await requestMicrophoneAccess()
      set({ microphonePermission: permission })
      const settings = await window.api.settings.get().catch(() => null)
      capture = await VoiceAudioCapture.start(settings?.voice?.deviceId, (level) => {
        const recording = get().recording
        if (recording?.threadId === threadId) set({ recording: { ...recording, level } })
      }, () => {
        set({ deviceFallback: true })
        void window.api.settings.set({ voice: { deviceId: '' } })
      })
      const startedAt = performance.now()
      set({
        recording: { threadId, startedAt, elapsedMs: 0, level: 0 },
        microphonePermission: 'granted',
        localErrors: { ...get().localErrors, [threadId]: undefined }
      })
      elapsedTimer = window.setInterval(() => {
        const recording = get().recording
        if (!recording || recording.threadId !== threadId) return
        const elapsedMs = Math.min(VOICE_MAX_DURATION_MS, performance.now() - recording.startedAt)
        set({ recording: { ...recording, elapsedMs } })
        if (elapsedMs >= VOICE_MAX_DURATION_MS) void get().stopRecording('insert')
      }, 100)
    } catch (error) {
      const code = error instanceof VoiceCaptureError ? error.code : 'device-missing'
      if (code === 'permission-denied') set({ microphonePermission: 'denied' })
      else setError(set, get, threadId, code)
    }
  },

  async stopRecording(intent) {
    const activeCapture = capture
    const recording = get().recording
    if (!activeCapture || !recording) return
    capture = null
    clearElapsedTimer()
    set({ recording: null })
    try {
      const audio = await activeCapture.stop()
      if (audio.durationMs < VOICE_MIN_DURATION_MS) return
      await window.api.voice.submitTranscription({
        threadId: recording.threadId,
        intent,
        durationMs: Math.min(VOICE_MAX_DURATION_MS, audio.durationMs),
        pcm16: audio.pcm16
      })
    } catch (error) {
      const code = normalizeRuntimeError(error)
      if (code !== 'invalid-audio') setError(set, get, recording.threadId, code)
    }
  },

  async abortRecording() {
    const activeCapture = capture
    capture = null
    clearElapsedTimer()
    set({ recording: null })
    await activeCapture?.abort()
  },

  async retry(sessionId) {
    await window.api.voice.retryTranscription(sessionId)
  },

  clearError(threadId) {
    set({ localErrors: { ...get().localErrors, [threadId]: undefined } })
  },

  reportError(threadId, code) {
    setError(set, get, threadId, code)
  }
}))

export function sessionForThread(snapshot: VoiceRuntimeSnapshot, threadId: string): VoiceSessionState | null {
  return snapshot.sessions.find((session) => session.threadId === threadId) ?? null
}

export function shouldUseCompactVoiceFooter(
  snapshot: VoiceRuntimeSnapshot,
  recordingThreadId: string | undefined,
  threadId: string
): boolean {
  if (recordingThreadId === threadId) return true
  const phase = sessionForThread(snapshot, threadId)?.phase
  return phase === 'queued' || phase === 'transcribing'
}

function clearElapsedTimer(): void {
  if (elapsedTimer != null) window.clearInterval(elapsedTimer)
  elapsedTimer = null
}

function setError(
  set: (partial: Partial<VoiceStoreState>) => void,
  get: () => VoiceStoreState,
  threadId: string,
  code: VoiceErrorCode
): void {
  set({ localErrors: { ...get().localErrors, [threadId]: code } })
}

function clearRecoverableDeviceErrors(
  localErrors: Record<string, VoiceErrorCode | undefined>
): Record<string, VoiceErrorCode | undefined> {
  return Object.fromEntries(Object.entries(localErrors).map(([threadId, code]) => [
    threadId,
    code === 'device-missing' || code === 'device-unavailable' ? undefined : code
  ]))
}

function normalizeRuntimeError(error: unknown): VoiceErrorCode {
  const message = error instanceof Error ? error.message : String(error)
  const code = message.split(':').at(-1)?.trim()
  if (code === 'queue-full' || code === 'model-missing' || code === 'model-damaged') return code
  if (code === 'permission-denied' || code === 'device-missing' || code === 'device-unavailable' || code === 'invalid-audio') return code
  return 'transcription-failed'
}

function threadExists(threadId: string): boolean {
  if (isAvailableComposerVoiceOrigin(threadId)) return true
  const state = useThreadStore.getState()
  return state.activeThread?.id === threadId || state.threadList.some((thread) => thread.id === threadId)
}
