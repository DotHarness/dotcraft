export const VOICE_SESSION_CAPACITY = 2 as const
export const VOICE_SAMPLE_RATE = 16_000 as const
export const VOICE_MIN_DURATION_MS = 250 as const
export const VOICE_MAX_DURATION_MS = 5 * 60 * 1_000

export type VoiceModelPhase = 'missing' | 'downloading' | 'installed' | 'damaged' | 'failed'

export type VoiceSessionPhase = 'recording' | 'queued' | 'transcribing' | 'retryable'

export type VoiceIntent = 'insert' | 'send'

export type VoiceErrorCode =
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

export interface VoiceModelState {
  phase: VoiceModelPhase
  bytesDownloaded: number
  bytesTotal: number | null
  errorCode?: VoiceErrorCode
}

export interface VoiceSessionState {
  sessionId: string
  threadId: string
  intent: VoiceIntent
  phase: VoiceSessionPhase
  durationMs: number
  errorCode?: VoiceErrorCode
}

export interface VoiceRuntimeSnapshot {
  model: VoiceModelState
  sessions: VoiceSessionState[]
  capacity: typeof VOICE_SESSION_CAPACITY
}

export type VoiceMicrophonePermissionStatus =
  | 'not-determined'
  | 'granted'
  | 'denied'
  | 'restricted'
  | 'unknown'

export interface VoiceTranscriptionInput {
  threadId: string
  intent: VoiceIntent
  durationMs: number
  pcm16: ArrayBuffer
}

export interface VoiceSessionEvent extends VoiceSessionState {
  type: 'changed' | 'completed' | 'discarded'
  transcript?: string
}

export interface VoiceDevicePreference {
  deviceId?: string
}

export interface VoiceApi {
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

export function isVoiceIntent(value: unknown): value is VoiceIntent {
  return value === 'insert' || value === 'send'
}
