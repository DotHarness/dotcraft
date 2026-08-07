import { mkdtemp, readdir, rm, writeFile } from 'fs/promises'
import { tmpdir } from 'os'
import { join } from 'path'
import { afterEach, describe, expect, it, vi } from 'vitest'

import type { VoiceModelState } from '../../../shared/voice'
import {
  VoiceRuntimeError,
  VoiceRuntimeService,
  type VoiceModelController
} from '../VoiceRuntimeService'
import type { VoiceTranscriber, VoiceTranscriptionResult } from '../VoiceWorkerClient'

const tempRoots: string[] = []

afterEach(async () => {
  await Promise.all(tempRoots.splice(0).map((path) => rm(path, { recursive: true, force: true })))
})

describe('VoiceRuntimeService', () => {
  it('serializes two sessions and rejects a third admission', async () => {
    const first = deferred<VoiceTranscriptionResult>()
    const second = deferred<VoiceTranscriptionResult>()
    const transcriber = fakeTranscriber([first.promise, second.promise])
    const service = await createService(transcriber)

    const a = await service.submitTranscription(input('thread-a'))
    const b = await service.submitTranscription(input('thread-b'))
    await expect(service.submitTranscription(input('thread-c')))
      .rejects.toMatchObject({ code: 'queue-full' } satisfies Partial<VoiceRuntimeError>)

    expect(service.getSnapshot().sessions).toEqual(expect.arrayContaining([
      expect.objectContaining({ sessionId: a.sessionId, phase: 'transcribing' }),
      expect.objectContaining({ sessionId: b.sessionId, phase: 'queued' })
    ]))
    expect(transcriber.transcribe).toHaveBeenCalledTimes(1)

    first.resolve({ transcript: 'first' })
    await vi.waitFor(() => expect(transcriber.transcribe).toHaveBeenCalledTimes(2))
    second.resolve({ transcript: 'second' })
    await vi.waitFor(() => expect(service.getSnapshot().sessions).toHaveLength(0))
    await service.shutdown()
  })

  it('atomically admits only two concurrent submissions', async () => {
    const first = deferred<VoiceTranscriptionResult>()
    const second = deferred<VoiceTranscriptionResult>()
    const transcriber = fakeTranscriber([first.promise, second.promise])
    const { service, voiceRoot } = await createServiceWithRoot(transcriber)

    const results = await Promise.allSettled([
      service.submitTranscription(input('thread-a')),
      service.submitTranscription(input('thread-b')),
      service.submitTranscription(input('thread-c'))
    ])

    expect(results.filter((result) => result.status === 'fulfilled')).toHaveLength(2)
    const rejected = results.find((result) => result.status === 'rejected')
    expect(rejected).toMatchObject({
      status: 'rejected',
      reason: expect.objectContaining({ code: 'queue-full' })
    })
    expect(service.getSnapshot().sessions).toHaveLength(2)
    expect(await readdir(join(voiceRoot, 'temp'))).toHaveLength(2)

    const admitted = results.flatMap((result) => result.status === 'fulfilled' ? [result.value] : [])
    await Promise.all(admitted.map(({ sessionId }) => service.discardSession(sessionId)))
    await service.shutdown()
  })

  it('releases a reserved slot and removes partial audio when WAV creation fails', async () => {
    const pending = deferred<VoiceTranscriptionResult>()
    const transcriber = fakeTranscriber([pending.promise])
    const voiceRoot = await mkdtemp(join(tmpdir(), 'dotcraft-voice-test-'))
    tempRoots.push(voiceRoot)
    const model = new FakeModel(join(voiceRoot, 'model.bin'))
    const writeWav = vi.fn(async (path: string) => {
      await writeFile(path, 'partial')
      if (writeWav.mock.calls.length === 1) throw new Error('write-failed')
    })
    const service = new VoiceRuntimeService({ voiceRoot, modelManager: model, transcriber, writeWav })
    await service.initialize()

    await expect(service.submitTranscription(input('failed'))).rejects.toThrow('write-failed')
    expect(await readdir(join(voiceRoot, 'temp'))).toEqual([])

    const admitted = await service.submitTranscription(input('recovered'))
    expect(service.getSnapshot().sessions).toHaveLength(1)
    await service.discardSession(admitted.sessionId)
    await service.shutdown()
  })

  it('invalidates a pending admission before removing the model', async () => {
    const transcriber = fakeTranscriber([])
    const { service, model, voiceRoot, writeStarted, releaseWrite } = await createServiceWithPendingAdmission(transcriber)
    const submission = service.submitTranscription(input('pending-remove')).catch((error) => error)
    await writeStarted.promise

    const removal = service.removeModel()
    releaseWrite.resolve(undefined)

    await expect(submission).resolves.toMatchObject({ code: 'model-missing' })
    await removal
    expect(service.getSnapshot().sessions).toEqual([])
    expect(model.getState().phase).toBe('missing')
    expect(transcriber.transcribe).not.toHaveBeenCalled()
    expect(await readdir(join(voiceRoot, 'temp'))).toEqual([])
  })

  it('reopens admission after repairing the model', async () => {
    const transcription = deferred<VoiceTranscriptionResult>()
    const transcriber = fakeTranscriber([transcription.promise])
    const { service, writeStarted, releaseWrite } = await createServiceWithPendingAdmission(transcriber)
    const submission = service.submitTranscription(input('pending-repair')).catch((error) => error)
    await writeStarted.promise

    const repair = service.repairModel()
    releaseWrite.resolve(undefined)

    await expect(submission).resolves.toMatchObject({ code: 'model-missing' })
    await repair
    const admitted = await service.submitTranscription(input('after-repair'))
    expect(transcriber.transcribe).toHaveBeenCalledTimes(1)
    await service.discardSession(admitted.sessionId)
    await service.shutdown()
  })

  it('invalidates a pending admission during shutdown', async () => {
    const transcriber = fakeTranscriber([])
    const { service, voiceRoot, writeStarted, releaseWrite } = await createServiceWithPendingAdmission(transcriber)
    const submission = service.submitTranscription(input('pending-shutdown')).catch((error) => error)
    await writeStarted.promise

    const shutdown = service.shutdown()
    releaseWrite.resolve(undefined)

    await expect(submission).resolves.toMatchObject({ code: 'model-missing' })
    await shutdown
    expect(service.getSnapshot().sessions).toEqual([])
    expect(transcriber.transcribe).not.toHaveBeenCalled()
    await expect(readdir(join(voiceRoot, 'temp'))).rejects.toMatchObject({ code: 'ENOENT' })
  })

  it('does not reopen admission when a later model removal is still running', async () => {
    const voiceRoot = await mkdtemp(join(tmpdir(), 'dotcraft-voice-test-'))
    tempRoots.push(voiceRoot)
    const model = new InterleavedLifecycleModel(join(voiceRoot, 'model.bin'))
    const transcriber = fakeTranscriber([])
    const service = new VoiceRuntimeService({ voiceRoot, modelManager: model, transcriber })
    await service.initialize()

    const repairing = service.repairModel()
    await model.repairStarted.promise
    const removing = service.removeModel()
    await model.removeStarted.promise
    model.releaseRepair.resolve(undefined)
    await repairing

    await expect(service.submitTranscription(input('during-remove')))
      .rejects.toMatchObject({ code: 'model-missing' })

    model.releaseRemove.resolve(undefined)
    await removing
    expect(model.getState().phase).toBe('missing')
    expect(service.getSnapshot().sessions).toEqual([])
    expect(transcriber.transcribe).not.toHaveBeenCalled()
    await service.shutdown()
  })

  it('keeps failed audio in the same slot for retry', async () => {
    const failed = deferred<VoiceTranscriptionResult>()
    const transcriber = fakeTranscriber([
      failed.promise,
      Promise.resolve({ transcript: 'recovered' })
    ])
    const service = await createService(transcriber)
    const events: string[] = []
    service.onSessionEvent((event) => events.push(`${event.sessionId}:${event.type}:${event.phase}`))

    const { sessionId } = await service.submitTranscription(input('thread-a'))
    failed.reject(new Error('failed'))
    await vi.waitFor(() => expect(service.getSnapshot().sessions[0]?.phase).toBe('retryable'))
    await service.retryTranscription(sessionId)
    await vi.waitFor(() => expect(service.getSnapshot().sessions).toHaveLength(0))

    expect(transcriber.transcribe).toHaveBeenCalledTimes(2)
    expect(events.some((entry) => entry.startsWith(`${sessionId}:completed`))).toBe(true)
    await service.shutdown()
  })

  it('discards retryable audio and ignores later work', async () => {
    const pending = deferred<VoiceTranscriptionResult>()
    const transcriber = fakeTranscriber([pending.promise])
    const service = await createService(transcriber)
    const { sessionId } = await service.submitTranscription(input('thread-a'))

    await service.discardSession(sessionId)
    expect(service.getSnapshot().sessions).toHaveLength(0)
    expect(transcriber.cancel).toHaveBeenCalledWith(sessionId)
    pending.resolve({ transcript: 'late' })
    await service.shutdown()
  })
})

async function createService(transcriber: VoiceTranscriber): Promise<VoiceRuntimeService> {
  return (await createServiceWithRoot(transcriber)).service
}

async function createServiceWithRoot(transcriber: VoiceTranscriber): Promise<{
  service: VoiceRuntimeService
  voiceRoot: string
}> {
  const voiceRoot = await mkdtemp(join(tmpdir(), 'dotcraft-voice-test-'))
  tempRoots.push(voiceRoot)
  const model = new FakeModel(join(voiceRoot, 'model.bin'))
  const service = new VoiceRuntimeService({ voiceRoot, modelManager: model, transcriber })
  await service.initialize()
  return { service, voiceRoot }
}

async function createServiceWithPendingAdmission(transcriber: VoiceTranscriber): Promise<{
  service: VoiceRuntimeService
  model: FakeModel
  voiceRoot: string
  writeStarted: ReturnType<typeof deferred<void>>
  releaseWrite: ReturnType<typeof deferred<void>>
}> {
  const voiceRoot = await mkdtemp(join(tmpdir(), 'dotcraft-voice-test-'))
  tempRoots.push(voiceRoot)
  const model = new FakeModel(join(voiceRoot, 'model.bin'))
  const writeStarted = deferred<void>()
  const releaseWrite = deferred<void>()
  let writeCount = 0
  const writeWav = async (path: string) => {
    writeCount += 1
    if (writeCount === 1) {
      writeStarted.resolve(undefined)
      await releaseWrite.promise
    }
    await writeFile(path, 'wav')
  }
  const service = new VoiceRuntimeService({ voiceRoot, modelManager: model, transcriber, writeWav })
  await service.initialize()
  return { service, model, voiceRoot, writeStarted, releaseWrite }
}

function input(threadId: string) {
  return {
    threadId,
    intent: 'insert' as const,
    durationMs: 500,
    pcm16: new Uint8Array(16_000).buffer
  }
}

class FakeModel implements VoiceModelController {
  private state: VoiceModelState = { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 }
  constructor(readonly modelPath: string) {}
  getState(): VoiceModelState { return { ...this.state } }
  subscribe(): () => void { return () => {} }
  async initialize(): Promise<void> {}
  async install(): Promise<void> { this.state = { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 } }
  async cancelInstall(): Promise<void> {}
  async remove(): Promise<void> { this.state = { phase: 'missing', bytesDownloaded: 0, bytesTotal: null } }
  async repair(): Promise<void> { await this.install() }
}

class InterleavedLifecycleModel implements VoiceModelController {
  private state: VoiceModelState = { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 }
  readonly repairStarted = deferred<void>()
  readonly releaseRepair = deferred<void>()
  readonly removeStarted = deferred<void>()
  readonly releaseRemove = deferred<void>()

  constructor(readonly modelPath: string) {}
  getState(): VoiceModelState { return { ...this.state } }
  subscribe(): () => void { return () => {} }
  async initialize(): Promise<void> {}
  async install(): Promise<void> { this.state = { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 } }
  async cancelInstall(): Promise<void> {}
  async remove(): Promise<void> {
    this.removeStarted.resolve(undefined)
    await this.releaseRemove.promise
    this.state = { phase: 'missing', bytesDownloaded: 0, bytesTotal: null }
  }
  async repair(): Promise<void> {
    this.repairStarted.resolve(undefined)
    await this.releaseRepair.promise
    this.state = { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 }
  }
}

function fakeTranscriber(results: Array<Promise<VoiceTranscriptionResult>>): VoiceTranscriber & {
  transcribe: ReturnType<typeof vi.fn>
  cancel: ReturnType<typeof vi.fn>
  shutdown: ReturnType<typeof vi.fn>
} {
  let index = 0
  return {
    transcribe: vi.fn(() => results[index++] ?? Promise.reject(new Error('missing fake result'))),
    cancel: vi.fn(async () => {}),
    shutdown: vi.fn(async () => {})
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}
