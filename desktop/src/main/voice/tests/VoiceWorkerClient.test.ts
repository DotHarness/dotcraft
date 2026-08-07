import { EventEmitter } from 'events'

import type { UtilityProcess } from 'electron'
import { describe, expect, it, vi } from 'vitest'

import {
  UtilityVoiceWorkerClient,
  VoiceWorkerError
} from '../VoiceWorkerClient'

describe('UtilityVoiceWorkerClient', () => {
  it('initializes once and returns an automatically detected language', async () => {
    const worker = new FakeUtilityProcess((message) => {
      if (message.method === 'initialize') {
        worker.respond(message.id, { runtime: 'whisper.cpp' })
      } else {
        worker.respond(message.id, { transcript: '  hello  ', language: 'en' })
      }
    })
    const fork = vi.fn(() => worker as unknown as UtilityProcess)
    const client = new UtilityVoiceWorkerClient({ modulePath: 'voiceWorker.js' }, fork)

    await expect(client.transcribe('session-a', 'a.wav', 'ggml-base.bin')).resolves.toEqual({
      transcript: 'hello',
      language: 'en'
    })
    await expect(client.transcribe('session-b', 'b.wav', 'ggml-base.bin')).resolves.toEqual({
      transcript: 'hello',
      language: 'en'
    })

    expect(fork).toHaveBeenCalledTimes(1)
    expect(worker.messages.map((message) => message.method)).toEqual([
      'initialize',
      'transcribe',
      'transcribe'
    ])
    await client.shutdown()
  })

  it('terminates the utility process when active transcription is cancelled', async () => {
    const worker = new FakeUtilityProcess((message) => {
      if (message.method === 'initialize') worker.respond(message.id, {})
    })
    const client = new UtilityVoiceWorkerClient(
      { modulePath: 'voiceWorker.js' },
      () => worker as unknown as UtilityProcess
    )
    const transcription = client.transcribe('session-a', 'a.wav', 'ggml-base.bin')
    await vi.waitFor(() => expect(worker.messages.at(-1)?.method).toBe('transcribe'))

    await client.cancel('session-a')

    await expect(transcription).rejects.toMatchObject({ code: 'cancelled' } satisfies Partial<VoiceWorkerError>)
    expect(worker.killed).toBe(true)
  })

  it('maps a worker crash and starts a fresh process for retry', async () => {
    const first = new FakeUtilityProcess((message) => {
      if (message.method === 'initialize') first.respond(message.id, {})
    })
    const second = new FakeUtilityProcess((message) => {
      if (message.method === 'initialize') second.respond(message.id, {})
      else second.respond(message.id, { transcript: 'recovered' })
    })
    const workers = [first, second]
    const fork = vi.fn(() => workers.shift() as unknown as UtilityProcess)
    const client = new UtilityVoiceWorkerClient({ modulePath: 'voiceWorker.js' }, fork)
    const failed = client.transcribe('session-a', 'a.wav', 'ggml-base.bin')
    await vi.waitFor(() => expect(first.messages.at(-1)?.method).toBe('transcribe'))

    first.crash()

    await expect(failed).rejects.toMatchObject({ code: 'worker-crashed' } satisfies Partial<VoiceWorkerError>)
    await expect(client.transcribe('session-a', 'a.wav', 'ggml-base.bin')).resolves.toEqual({
      transcript: 'recovered',
      language: undefined
    })
    expect(fork).toHaveBeenCalledTimes(2)
    await client.shutdown()
  })
})

interface WorkerMessage {
  id: string
  method: 'initialize' | 'transcribe'
  params: Record<string, unknown>
}

class FakeUtilityProcess extends EventEmitter {
  pid: number | undefined = 42
  readonly stdout = null
  readonly stderr = null
  readonly messages: WorkerMessage[] = []
  killed = false

  constructor(private readonly handleMessage: (message: WorkerMessage) => void) {
    super()
  }

  postMessage(message: WorkerMessage): void {
    this.messages.push(message)
    queueMicrotask(() => this.handleMessage(message))
  }

  respond(id: string, result: Record<string, unknown>): void {
    this.emit('message', { id, ok: true, result })
  }

  crash(): void {
    this.pid = undefined
    this.emit('exit', 1)
  }

  kill(): boolean {
    this.killed = true
    this.pid = undefined
    this.emit('exit', 0)
    return true
  }
}
