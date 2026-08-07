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
import {
  appendVoiceTranscript,
  captureComposerVoiceSubmitter,
  isAvailableComposerVoiceOrigin,
  releaseComposerVoiceSubmitter,
  releaseComposerVoiceSubmittersForOrigin,
  retainComposerVoiceSubmitter
} from './composerDraftBridge'
import { requestMicrophoneAccess } from './microphoneAccess'
import { registerVoiceOriginCleanup } from './voiceOriginCleanupBridge'

interface RecordingState {
  threadId: string
  startedAt: number
  elapsedMs: number
  level: number
}

interface VoiceFinalizingState {
  threadId: string
  intent: VoiceIntent
  durationMs: number
  transitionId?: symbol
  existingSessionIds?: string[]
  sessionId?: string
}

interface VoiceStoreState {
  initialized: boolean
  snapshot: VoiceRuntimeSnapshot
  recording: RecordingState | null
  finalizing: VoiceFinalizingState | null
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
  cancelRecordingStart(threadId: string): void
  stopRecording(intent: VoiceIntent): Promise<void>
  abortRecording(): Promise<void>
  discardOrigin(threadId: string): Promise<void>
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
let captureStartup: { token: symbol; threadId: string } | null = null
let elapsedTimer: number | null = null
let focusListenerAttached = false
let originCleanupAttached = false
const originVersions = new Map<string, number>()

export const useVoiceStore = create<VoiceStoreState>((set, get) => ({
  initialized: false,
  snapshot: EMPTY_SNAPSHOT,
  recording: null,
  finalizing: null,
  microphonePermission: 'unknown',
  deviceFallback: false,
  localErrors: {},

  initialize() {
    if (get().initialized) return
    if (!window.api?.voice) return
    set({ initialized: true })
    void window.api.voice.getSnapshot().then((snapshot) => applySnapshot(set, snapshot)).catch(() => {})
    void get().refreshMicrophonePermission()
    if (typeof window.api.voice.onSnapshot === 'function') {
      window.api.voice.onSnapshot((snapshot) => applySnapshot(set, snapshot))
    }
    if (typeof window.api.voice.onSessionEvent === 'function') {
      window.api.voice.onSessionEvent((event) => {
        if (event.type === 'completed' || event.type === 'discarded') {
          set((state) => ({
            finalizing: state.finalizing && matchesFinalizingSession(state.finalizing, event)
              ? null
              : state.finalizing
          }))
        }
        if (event.type === 'discarded') {
          releaseComposerVoiceSubmitter(event.sessionId)
          return
        }
        if (event.type !== 'completed') return
        if (!threadExists(event.threadId)) {
          releaseComposerVoiceSubmitter(event.sessionId)
          return
        }
        void appendVoiceTranscript(
          event.threadId,
          event.transcript ?? '',
          event.intent === 'send',
          event.sessionId
        ).finally(() => releaseComposerVoiceSubmitter(event.sessionId))
      })
    }
    if (!originCleanupAttached) {
      originCleanupAttached = true
      registerVoiceOriginCleanup((threadIds) => {
        for (const threadId of threadIds) void useVoiceStore.getState().discardOrigin(threadId)
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
    if (capture || captureStartup || get().recording) return
    const startup = { token: Symbol('voice-capture-start'), threadId }
    captureStartup = startup
    try {
      set({ localErrors: { ...get().localErrors, [threadId]: undefined } })
      const retryable = get().snapshot.sessions.find((session) => session.phase === 'retryable')
      if (retryable) await window.api.voice.discardSession(retryable.sessionId)
      if (!isCurrentStartup(startup)) return
      const unresolved = get().snapshot.sessions.filter((session) => (
        session.phase !== 'recording' && session.sessionId !== retryable?.sessionId
      )).length + (get().finalizing ? 1 : 0)
      if (unresolved >= get().snapshot.capacity) {
        setError(set, get, threadId, 'queue-full')
        return
      }

      const permission = await requestMicrophoneAccess()
      if (!isCurrentStartup(startup)) return
      set({ microphonePermission: permission })
      const settings = await window.api.settings.get().catch(() => null)
      if (!isCurrentStartup(startup)) return
      const startedCapture = await VoiceAudioCapture.start(settings?.voice?.deviceId, (level) => {
        const recording = get().recording
        if (recording?.threadId === threadId) set({ recording: { ...recording, level } })
      }, () => {
        set({ deviceFallback: true })
        void window.api.settings.set({ voice: { deviceId: '' } })
      })
      if (!isCurrentStartup(startup)) {
        await startedCapture.abort()
        return
      }
      capture = startedCapture
      captureStartup = null
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
      if (!isCurrentStartup(startup)) return
      const code = error instanceof VoiceCaptureError ? error.code : 'device-missing'
      if (code === 'permission-denied') set({ microphonePermission: 'denied' })
      else setError(set, get, threadId, code)
    } finally {
      if (isCurrentStartup(startup)) captureStartup = null
    }
  },

  cancelRecordingStart(threadId) {
    if (captureStartup?.threadId === threadId) captureStartup = null
  },

  async stopRecording(intent) {
    const activeCapture = capture
    const recording = get().recording
    if (!activeCapture || !recording) return
    const transitionId = Symbol('voice-finalizing')
    const originVersion = getOriginVersion(recording.threadId)
    const retainedSubmitter = intent === 'send'
      ? captureComposerVoiceSubmitter(recording.threadId)
      : null
    capture = null
    clearElapsedTimer()
    set({
      recording: null,
      finalizing: {
        threadId: recording.threadId,
        intent,
        durationMs: recording.elapsedMs,
        transitionId,
        existingSessionIds: get().snapshot.sessions
          .filter((session) => session.threadId === recording.threadId)
          .map((session) => session.sessionId)
      }
    })
    try {
      const audio = await activeCapture.stop()
      if (getOriginVersion(recording.threadId) !== originVersion) return
      if (audio.durationMs < VOICE_MIN_DURATION_MS) {
        clearFinalizing(set, transitionId)
        return
      }
      set((state) => ({
        finalizing: state.finalizing?.transitionId === transitionId
          ? { ...state.finalizing, durationMs: Math.min(VOICE_MAX_DURATION_MS, audio.durationMs) }
          : state.finalizing
      }))
      const { sessionId } = await window.api.voice.submitTranscription({
        threadId: recording.threadId,
        intent,
        durationMs: Math.min(VOICE_MAX_DURATION_MS, audio.durationMs),
        pcm16: audio.pcm16
      })
      if (getOriginVersion(recording.threadId) !== originVersion) {
        await window.api.voice.discardSession(sessionId)
        return
      }
      if (retainedSubmitter) {
        retainComposerVoiceSubmitter(sessionId, recording.threadId, retainedSubmitter)
      }
      set((state) => {
        if (state.finalizing?.transitionId !== transitionId) return {}
        const finalizing = { ...state.finalizing, sessionId }
        const admitted = state.snapshot.sessions.some((session) => (
          session.sessionId === sessionId && isProcessingSession(session)
        ))
        return { finalizing: admitted ? null : finalizing }
      })
      const snapshot = await window.api.voice.getSnapshot().catch(() => null)
      if (getOriginVersion(recording.threadId) !== originVersion) {
        await window.api.voice.discardSession(sessionId)
        return
      }
      if (snapshot) applySnapshot(set, snapshot)
    } catch (error) {
      clearFinalizing(set, transitionId)
      const code = normalizeRuntimeError(error)
      if (code !== 'invalid-audio') setError(set, get, recording.threadId, code)
    }
  },

  async abortRecording() {
    captureStartup = null
    const activeCapture = capture
    capture = null
    clearElapsedTimer()
    set({ recording: null })
    await activeCapture?.abort()
  },

  async discardOrigin(threadId) {
    originVersions.set(threadId, getOriginVersion(threadId) + 1)
    if (captureStartup?.threadId === threadId) captureStartup = null
    const activeCapture = get().recording?.threadId === threadId ? capture : null
    if (activeCapture) {
      capture = null
      clearElapsedTimer()
    }
    set((state) => ({
      recording: state.recording?.threadId === threadId ? null : state.recording,
      finalizing: state.finalizing?.threadId === threadId ? null : state.finalizing,
      localErrors: { ...state.localErrors, [threadId]: undefined }
    }))
    releaseComposerVoiceSubmittersForOrigin(threadId)
    await activeCapture?.abort()
    const sessions = get().snapshot.sessions.filter((session) => session.threadId === threadId)
    const discardSession = window.api?.voice?.discardSession
    if (typeof discardSession === 'function') {
      await Promise.all(sessions.map((session) => discardSession(session.sessionId)))
    }
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
  finalizingThreadId: string | undefined,
  threadId: string
): boolean {
  if (recordingThreadId === threadId || finalizingThreadId === threadId) return true
  return isVoiceProcessingForThread(snapshot, undefined, threadId)
}

export function isVoiceProcessingForThread(
  snapshot: VoiceRuntimeSnapshot,
  finalizingThreadId: string | undefined,
  threadId: string
): boolean {
  if (finalizingThreadId === threadId) return true
  const phase = sessionForThread(snapshot, threadId)?.phase
  return phase === 'queued' || phase === 'transcribing'
}

function clearElapsedTimer(): void {
  if (elapsedTimer != null) window.clearInterval(elapsedTimer)
  elapsedTimer = null
}

function isCurrentStartup(startup: { token: symbol; threadId: string }): boolean {
  return captureStartup?.token === startup.token
}

function getOriginVersion(threadId: string): number {
  return originVersions.get(threadId) ?? 0
}

function applySnapshot(
  set: (update: Partial<VoiceStoreState> | ((state: VoiceStoreState) => Partial<VoiceStoreState>)) => void,
  snapshot: VoiceRuntimeSnapshot
): void {
  set((state) => {
    const admitted = state.finalizing != null && snapshot.sessions.some((session) => (
      isProcessingSession(session) && matchesFinalizingSession(state.finalizing!, session)
    ))
    return {
      snapshot,
      finalizing: admitted ? null : state.finalizing
    }
  })
}

function clearFinalizing(
  set: (update: Partial<VoiceStoreState> | ((state: VoiceStoreState) => Partial<VoiceStoreState>)) => void,
  transitionId: symbol
): void {
  set((state) => ({
    finalizing: state.finalizing?.transitionId === transitionId ? null : state.finalizing
  }))
}

function isProcessingSession(session: VoiceSessionState): boolean {
  return session.phase === 'queued' || session.phase === 'transcribing' || session.phase === 'retryable'
}

function matchesFinalizingSession(
  finalizing: VoiceFinalizingState,
  session: Pick<VoiceSessionState, 'sessionId' | 'threadId'>
): boolean {
  if (session.threadId !== finalizing.threadId) return false
  if (finalizing.sessionId) return session.sessionId === finalizing.sessionId
  return !(finalizing.existingSessionIds ?? []).includes(session.sessionId)
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
