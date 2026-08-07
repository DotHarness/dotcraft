import { randomUUID } from 'crypto'
import { existsSync } from 'fs'
import { mkdir, rm } from 'fs/promises'
import { join } from 'path'

import {
  VOICE_MAX_DURATION_MS,
  VOICE_MIN_DURATION_MS,
  VOICE_SESSION_CAPACITY,
  isVoiceIntent,
  type VoiceErrorCode,
  type VoiceRuntimeSnapshot,
  type VoiceSessionEvent,
  type VoiceSessionState,
  type VoiceTranscriptionInput
} from '../../shared/voice'
import {
  VoiceWorkerError,
  type VoiceTranscriber
} from './VoiceWorkerClient'
import { writeMonoPcm16Wav } from './wav'

interface SessionRecord extends VoiceSessionState {
  wavPath: string
  discarded: boolean
}

type SnapshotListener = (snapshot: VoiceRuntimeSnapshot) => void
type SessionListener = (event: VoiceSessionEvent) => void

export interface VoiceRuntimeServiceOptions {
  voiceRoot: string
  modelManager: VoiceModelController
  transcriber: VoiceTranscriber
  writeWav?: (path: string, pcm16: Uint8Array) => Promise<void>
}

export interface VoiceModelController {
  readonly modelPath: string
  getState(): VoiceRuntimeSnapshot['model']
  subscribe(listener: (state: VoiceRuntimeSnapshot['model']) => void): () => void
  initialize(): Promise<void>
  install(): Promise<void>
  cancelInstall(): Promise<void>
  remove(): Promise<void>
  repair(): Promise<void>
}

export class VoiceRuntimeError extends Error {
  constructor(readonly code: VoiceErrorCode) {
    super(code)
    this.name = 'VoiceRuntimeError'
  }
}

export class VoiceRuntimeService {
  private readonly sessions = new Map<string, SessionRecord>()
  private readonly queue: string[] = []
  private readonly snapshotListeners = new Set<SnapshotListener>()
  private readonly sessionListeners = new Set<SessionListener>()
  private readonly tempRoot: string
  private runningSessionId: string | null = null
  private pendingAdmissions = 0
  private shuttingDown = false

  constructor(private readonly options: VoiceRuntimeServiceOptions) {
    this.tempRoot = join(options.voiceRoot, 'temp')
    options.modelManager.subscribe(() => this.emitSnapshot())
  }

  async initialize(): Promise<void> {
    await rm(this.tempRoot, { recursive: true, force: true })
    await mkdir(this.tempRoot, { recursive: true })
    await this.options.modelManager.initialize()
  }

  getSnapshot(): VoiceRuntimeSnapshot {
    return {
      model: this.options.modelManager.getState(),
      sessions: [...this.sessions.values()].map(toPublicSession),
      capacity: VOICE_SESSION_CAPACITY
    }
  }

  onSnapshot(listener: SnapshotListener): () => void {
    this.snapshotListeners.add(listener)
    return () => this.snapshotListeners.delete(listener)
  }

  onSessionEvent(listener: SessionListener): () => void {
    this.sessionListeners.add(listener)
    return () => this.sessionListeners.delete(listener)
  }

  async installModel(): Promise<void> {
    await this.options.modelManager.install()
  }

  async cancelModelInstall(): Promise<void> {
    await this.options.modelManager.cancelInstall()
  }

  async removeModel(): Promise<void> {
    await this.discardAllSessions()
    await this.options.transcriber.shutdown()
    await this.options.modelManager.remove()
  }

  async repairModel(): Promise<void> {
    await this.discardAllSessions()
    await this.options.transcriber.shutdown()
    await this.options.modelManager.repair()
  }

  async submitTranscription(input: VoiceTranscriptionInput): Promise<{ sessionId: string }> {
    this.validateInput(input)
    const modelState = this.options.modelManager.getState()
    if (modelState.phase !== 'installed') {
      throw new VoiceRuntimeError(modelState.phase === 'damaged' ? 'model-damaged' : 'model-missing')
    }
    if (this.sessions.size + this.pendingAdmissions >= VOICE_SESSION_CAPACITY) {
      throw new VoiceRuntimeError('queue-full')
    }

    const sessionId = randomUUID()
    const wavPath = join(this.tempRoot, `${sessionId}.wav`)
    this.pendingAdmissions += 1
    try {
      await mkdir(this.tempRoot, { recursive: true })
      await (this.options.writeWav ?? writeMonoPcm16Wav)(wavPath, new Uint8Array(input.pcm16))
      const session: SessionRecord = {
        sessionId,
        threadId: input.threadId.trim(),
        intent: input.intent,
        phase: 'queued',
        durationMs: Math.round(input.durationMs),
        wavPath,
        discarded: false
      }
      this.sessions.set(sessionId, session)
      this.queue.push(sessionId)
      this.emitSession({ ...toPublicSession(session), type: 'changed' })
      this.emitSnapshot()
      void this.drainQueue()
      return { sessionId }
    } catch (error) {
      await rm(wavPath, { force: true }).catch(() => {})
      throw error
    } finally {
      this.pendingAdmissions -= 1
    }
  }

  async retryTranscription(sessionId: string): Promise<void> {
    const session = this.sessions.get(sessionId)
    if (!session || session.phase !== 'retryable' || !existsSync(session.wavPath)) {
      throw new VoiceRuntimeError('invalid-audio')
    }
    session.phase = 'queued'
    delete session.errorCode
    if (!this.queue.includes(sessionId)) this.queue.push(sessionId)
    this.emitSession({ ...toPublicSession(session), type: 'changed' })
    this.emitSnapshot()
    void this.drainQueue()
  }

  async discardSession(sessionId: string): Promise<void> {
    const session = this.sessions.get(sessionId)
    if (!session) return
    session.discarded = true
    this.removeFromQueue(sessionId)
    if (this.runningSessionId === sessionId) await this.options.transcriber.cancel(sessionId)
    this.sessions.delete(sessionId)
    await rm(session.wavPath, { force: true })
    this.emitSession({ ...toPublicSession(session), type: 'discarded' })
    this.emitSnapshot()
  }

  async shutdown(): Promise<void> {
    if (this.shuttingDown) return
    this.shuttingDown = true
    try {
      await this.discardAllSessions()
      await this.options.transcriber.shutdown()
      await rm(this.tempRoot, { recursive: true, force: true })
    } finally {
      this.shuttingDown = false
    }
  }

  private async drainQueue(): Promise<void> {
    if (this.runningSessionId || this.shuttingDown) return
    while (this.queue.length > 0 && !this.runningSessionId && !this.shuttingDown) {
      const sessionId = this.queue.shift()!
      const session = this.sessions.get(sessionId)
      if (!session || session.discarded) continue
      this.runningSessionId = sessionId
      session.phase = 'transcribing'
      this.emitSession({ ...toPublicSession(session), type: 'changed' })
      this.emitSnapshot()
      try {
        const result = await this.options.transcriber.transcribe(
          sessionId,
          session.wavPath,
          this.options.modelManager.modelPath
        )
        if (session.discarded || !this.sessions.has(sessionId)) continue
        this.sessions.delete(sessionId)
        this.emitSession({
          ...toPublicSession(session),
          type: 'completed',
          transcript: result.transcript.trim()
        })
        await rm(session.wavPath, { force: true })
      } catch (error) {
        if (session.discarded || !this.sessions.has(sessionId)) continue
        session.phase = 'retryable'
        session.errorCode = mapWorkerError(error)
        this.emitSession({ ...toPublicSession(session), type: 'changed' })
      } finally {
        if (this.runningSessionId === sessionId) this.runningSessionId = null
        this.emitSnapshot()
      }
    }
  }

  private validateInput(input: VoiceTranscriptionInput): void {
    if (!input || typeof input !== 'object') throw new VoiceRuntimeError('invalid-audio')
    if (typeof input.threadId !== 'string' || input.threadId.trim().length === 0 || input.threadId.length > 256) {
      throw new VoiceRuntimeError('invalid-audio')
    }
    if (!isVoiceIntent(input.intent)) throw new VoiceRuntimeError('invalid-audio')
    if (!Number.isFinite(input.durationMs)
      || input.durationMs < VOICE_MIN_DURATION_MS
      || input.durationMs > VOICE_MAX_DURATION_MS) {
      throw new VoiceRuntimeError('invalid-audio')
    }
    if (!(input.pcm16 instanceof ArrayBuffer)
      || input.pcm16.byteLength === 0
      || input.pcm16.byteLength % 2 !== 0
      || input.pcm16.byteLength > 10_000_000) {
      throw new VoiceRuntimeError('invalid-audio')
    }
  }

  private async discardAllSessions(): Promise<void> {
    for (const sessionId of [...this.sessions.keys()]) await this.discardSession(sessionId)
  }

  private removeFromQueue(sessionId: string): void {
    let index = this.queue.indexOf(sessionId)
    while (index >= 0) {
      this.queue.splice(index, 1)
      index = this.queue.indexOf(sessionId)
    }
  }

  private emitSnapshot(): void {
    const snapshot = this.getSnapshot()
    for (const listener of this.snapshotListeners) listener(snapshot)
  }

  private emitSession(event: VoiceSessionEvent): void {
    for (const listener of this.sessionListeners) listener(event)
  }
}

function toPublicSession(session: SessionRecord): VoiceSessionState {
  return {
    sessionId: session.sessionId,
    threadId: session.threadId,
    intent: session.intent,
    phase: session.phase,
    durationMs: session.durationMs,
    errorCode: session.errorCode
  }
}

function mapWorkerError(error: unknown): VoiceErrorCode {
  if (error instanceof VoiceWorkerError) {
    if (error.code === 'worker-crashed') return 'worker-crashed'
    if (error.code === 'worker-unavailable') return 'worker-unavailable'
    if (error.code === 'invalid-audio') return 'invalid-audio'
    if (error.code === 'cancelled') return 'cancelled'
  }
  return 'transcription-failed'
}
