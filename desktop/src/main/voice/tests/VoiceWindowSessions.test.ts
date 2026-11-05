import { mkdtemp, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { VoiceRuntimeService, type VoiceSessionContext } from '../VoiceRuntimeService'
import type { VoiceTranscriber, VoiceTranscriptionResult } from '../VoiceWorkerClient'

const roots: string[] = []
afterEach(async () => { await Promise.all(roots.splice(0).map((root) => rm(root, { recursive: true, force: true }))) })

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: Error) => void
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

function transcriber() {
  const result = deferred<VoiceTranscriptionResult>()
  const started = deferred<void>()
  const worker: VoiceTranscriber = {
    transcribe: vi.fn(async () => { started.resolve(); return result.promise }),
    cancel: vi.fn(async () => { result.reject(new Error('cancelled')) }),
    shutdown: vi.fn(async () => {})
  }
  return { worker, started, result }
}

async function service(writeWav?: (path: string, data: Uint8Array) => Promise<void>, localWorker?: VoiceTranscriber) {
  const root = await mkdtemp(join(tmpdir(), 'voice-owner-test-'))
  roots.push(root)
  const local = transcriber()
  const runtime = new VoiceRuntimeService({
    voiceRoot: root,
    modelManager: {
      getState: () => ({ phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 }),
      subscribe: () => () => {}, initialize: async () => {}, install: async () => {},
      cancelInstall: async () => {}, remove: async () => {}, repair: async () => {}
    },
    localTranscriber: localWorker ?? local.worker,
    chatGpt: { transcriber: local.worker, isSignedIn: async () => false, isEnabled: () => false },
    writeWav: writeWav ?? (async (path) => { await writeFile(path, 'wav') })
  })
  await runtime.initialize()
  return runtime
}

const input = (threadId: string) => ({ threadId, intent: 'insert' as const, durationMs: 500, pcm16: new Uint8Array(16000).buffer })
const context = (ownerId: number, worker: VoiceTranscriber, controller = new AbortController()): VoiceSessionContext => ({
  ownerId, signal: controller.signal,
  chatGpt: { transcriber: worker, isSignedIn: async () => true, isEnabled: () => true }
})

describe('shared voice queue window ownership', () => {
  it('selects the current owner route when a queued session actually starts', async () => {
    const local = transcriber()
    const runtime = await service(undefined, local.worker)
    const first = transcriber()
    const second = transcriber()
    let enabled = true
    const secondContext = context(2, second.worker)
    secondContext.chatGpt!.isEnabled = () => enabled
    await runtime.submitTranscription(input('first'), context(1, first.worker))
    await first.started.promise
    await runtime.submitTranscription(input('queued'), secondContext)
    enabled = false
    first.result.resolve({ transcript: 'first' })
    await local.started.promise
    expect(second.worker.transcribe).not.toHaveBeenCalled()
    local.result.resolve({ transcript: 'local fallback' })
    await vi.waitFor(() => expect(runtime.getSnapshot().sessions).toEqual([]))
    await runtime.shutdown()
  })

  it('rechecks owner authentication and route selection on retry', async () => {
    const local = transcriber()
    const remote = transcriber()
    const runtime = await service(undefined, local.worker)
    let signedIn = false
    const owner = context(5, remote.worker)
    owner.chatGpt!.isSignedIn = async () => signedIn
    const session = await runtime.submitTranscription(input('retry'), owner)
    await local.started.promise
    local.result.reject(new Error('temporary local failure'))
    await vi.waitFor(() => expect(runtime.getSnapshot(5).sessions[0]?.phase).toBe('retryable'))
    signedIn = true
    await runtime.retryTranscription(session.sessionId, 5)
    await remote.started.promise
    remote.result.resolve({ transcript: 'signed in after admission' })
    await vi.waitFor(() => expect(runtime.getSnapshot().sessions).toEqual([]))
    await runtime.shutdown()
  })

  it('pins each session route and closes only the matching owner sessions', async () => {
    const runtime = await service()
    const first = transcriber()
    const second = transcriber()
    const events: Array<{ ownerId?: number; type: string; transcript?: string }> = []
    runtime.onSessionEvent((event, ownerId) => events.push({ ownerId, type: event.type, transcript: event.transcript }))
    const a = await runtime.submitTranscription(input('thread-a'), context(1, first.worker))
    const b = await runtime.submitTranscription(input('thread-b'), context(2, second.worker))
    expect(runtime.getSnapshot(1).sessions.map((session) => session.sessionId)).toEqual([a.sessionId])
    expect(runtime.getSnapshot(2).sessions.map((session) => session.sessionId)).toEqual([b.sessionId])
    await expect(runtime.discardSession(b.sessionId, 1)).rejects.toMatchObject({ code: 'invalid-audio' })
    await runtime.discardOwnerSessions(1)
    await second.started.promise
    expect(first.worker.cancel).toHaveBeenCalledWith(a.sessionId)
    expect(second.worker.cancel).not.toHaveBeenCalled()
    second.result.resolve({ transcript: 'window two' })
    await vi.waitFor(() => expect(events).toContainEqual({ ownerId: 2, type: 'completed', transcript: 'window two' }))
    expect(events.filter((event) => event.type === 'completed')).toEqual([{ ownerId: 2, type: 'completed', transcript: 'window two' }])
    await runtime.shutdown()
  })

  it('does not admit audio after its window closes during an asynchronous write', async () => {
    const writing = deferred<void>()
    const release = deferred<void>()
    const runtime = await service(async (path) => {
      writing.resolve()
      await release.promise
      await writeFile(path, 'wav')
    })
    const worker = transcriber()
    const controller = new AbortController()
    const submission = runtime.submitTranscription(input('closing'), context(7, worker.worker, controller))
    const rejected = expect(submission).rejects.toMatchObject({ name: 'AbortError' })
    await writing.promise
    controller.abort()
    release.resolve()
    await rejected
    expect(runtime.getSnapshot().sessions).toEqual([])
    expect(worker.worker.transcribe).not.toHaveBeenCalled()
    await runtime.shutdown()
  })
})
