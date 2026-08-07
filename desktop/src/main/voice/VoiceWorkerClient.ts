import { utilityProcess, type UtilityProcess } from 'electron'
import { randomUUID } from 'crypto'

export interface VoiceTranscriptionResult {
  transcript: string
  language?: string
}

export interface VoiceTranscriber {
  transcribe(sessionId: string, wavPath: string, modelPath: string): Promise<VoiceTranscriptionResult>
  cancel(sessionId: string): Promise<void>
  shutdown(): Promise<void>
}

export interface VoiceWorkerLaunch {
  modulePath: string
}

interface WorkerResponse {
  id?: string
  ok?: boolean
  result?: Record<string, unknown>
  error?: { code?: string }
}

interface PendingRequest {
  sessionId?: string
  resolve: (value: Record<string, unknown>) => void
  reject: (error: Error) => void
}

type ForkVoiceWorker = (modulePath: string) => UtilityProcess

export class VoiceWorkerError extends Error {
  constructor(readonly code: string) {
    super(code)
    this.name = 'VoiceWorkerError'
  }
}

export class UtilityVoiceWorkerClient implements VoiceTranscriber {
  private child: UtilityProcess | null = null
  private initializedModelPath: string | null = null
  private readonly pending = new Map<string, PendingRequest>()
  private stopping = false

  constructor(
    private readonly launch: VoiceWorkerLaunch,
    private readonly forkWorker: ForkVoiceWorker = defaultForkVoiceWorker
  ) {}

  async transcribe(
    sessionId: string,
    wavPath: string,
    modelPath: string
  ): Promise<VoiceTranscriptionResult> {
    await this.ensureStarted(modelPath)
    const result = await this.request('transcribe', { sessionId, wavPath }, sessionId)
    return {
      transcript: typeof result.transcript === 'string' ? result.transcript.trim() : '',
      language: typeof result.language === 'string' ? result.language : undefined
    }
  }

  async cancel(sessionId: string): Promise<void> {
    const pending = [...this.pending.entries()]
      .find(([, request]) => request.sessionId === sessionId)
    if (!pending) return
    const [requestId, request] = pending
    this.pending.delete(requestId)
    request.reject(new VoiceWorkerError('cancelled'))
    this.terminateChild()
  }

  async shutdown(): Promise<void> {
    this.stopping = true
    try {
      for (const request of this.pending.values()) {
        request.reject(new VoiceWorkerError('cancelled'))
      }
      this.pending.clear()
      this.terminateChild()
    } finally {
      this.stopping = false
    }
  }

  private async ensureStarted(modelPath: string): Promise<void> {
    if (this.child && this.initializedModelPath === modelPath) return
    if (this.child) await this.shutdown()

    const child = this.forkWorker(this.launch.modulePath)
    this.child = child
    child.on('message', (message) => this.handleMessage(child, message))
    child.once('error', () => this.handleCrash(child))
    child.once('exit', () => this.handleCrash(child))
    child.stderr?.resume()
    child.stdout?.resume()

    try {
      await waitForSpawn(child)
      await this.request('initialize', { protocolVersion: 1, modelPath })
      this.initializedModelPath = modelPath
    } catch (error) {
      if (this.child === child) this.terminateChild()
      throw error
    }
  }

  private request(
    method: 'initialize' | 'transcribe',
    params: Record<string, unknown>,
    sessionId?: string
  ): Promise<Record<string, unknown>> {
    const child = this.child
    if (!child || child.pid == null) {
      return Promise.reject(new VoiceWorkerError('worker-unavailable'))
    }
    const id = randomUUID()
    return new Promise<Record<string, unknown>>((resolve, reject) => {
      this.pending.set(id, { sessionId, resolve, reject })
      try {
        child.postMessage({ id, method, params })
      } catch {
        this.pending.delete(id)
        reject(new VoiceWorkerError('worker-unavailable'))
      }
    })
  }

  private handleMessage(child: UtilityProcess, message: unknown): void {
    if (this.child !== child || !isWorkerResponse(message) || !message.id) return
    const pending = this.pending.get(message.id)
    if (!pending) return
    this.pending.delete(message.id)
    if (message.ok === true) {
      pending.resolve(message.result ?? {})
      return
    }
    pending.reject(new VoiceWorkerError(normalizeWorkerCode(message.error?.code)))
  }

  private handleCrash(child: UtilityProcess): void {
    if (this.child !== child) return
    const error = new VoiceWorkerError(this.stopping ? 'cancelled' : 'worker-crashed')
    for (const pending of this.pending.values()) pending.reject(error)
    this.pending.clear()
    this.resetChild()
  }

  private terminateChild(): void {
    const child = this.child
    this.resetChild()
    if (child?.pid != null) child.kill()
  }

  private resetChild(): void {
    this.child = null
    this.initializedModelPath = null
  }
}

function defaultForkVoiceWorker(modulePath: string): UtilityProcess {
  return utilityProcess.fork(modulePath, [], {
    serviceName: 'DotCraft Voice Input',
    stdio: ['ignore', 'pipe', 'pipe']
  })
}

function waitForSpawn(child: UtilityProcess): Promise<void> {
  if (child.pid != null) return Promise.resolve()
  return new Promise((resolve, reject) => {
    const onSpawn = (): void => {
      cleanup()
      resolve()
    }
    const onExit = (): void => {
      cleanup()
      reject(new VoiceWorkerError('worker-unavailable'))
    }
    const cleanup = (): void => {
      child.off('spawn', onSpawn)
      child.off('exit', onExit)
    }
    child.once('spawn', onSpawn)
    child.once('exit', onExit)
  })
}

function isWorkerResponse(value: unknown): value is WorkerResponse {
  return typeof value === 'object' && value !== null
}

function normalizeWorkerCode(code: string | undefined): string {
  if (code === 'cancelled' || code === 'invalid-audio' || code === 'transcription-failed') return code
  if (code === 'worker-unavailable' || code === 'worker-crashed') return code
  return 'transcription-failed'
}
