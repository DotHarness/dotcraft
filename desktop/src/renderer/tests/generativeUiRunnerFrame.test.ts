// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  GENERATIVE_UI_CONNECT_METHOD,
  GENERATIVE_UI_LIMITS,
  GENERATIVE_UI_PROTOCOL_VERSION,
  GENERATIVE_UI_WORKER_SOURCE_ELEMENT_ID
} from '../components/conversation/generativeUi/generativeUiProtocol'

class FakeWorker extends EventTarget {
  static last: FakeWorker
  pings: string[] = []
  terminated = false

  constructor() {
    super()
    FakeWorker.last = this
  }

  postMessage(data: { type?: string; challenge?: string }): void {
    if (data.type === 'ping' && data.challenge) this.pings.push(data.challenge)
  }

  terminate(): void {
    this.terminated = true
  }

  send(data: unknown): void {
    this.dispatchEvent(new MessageEvent('message', { data }))
  }
}

beforeEach(async () => {
  vi.useFakeTimers()
  vi.resetModules()
  vi.stubGlobal('Worker', FakeWorker)
  Object.defineProperty(URL, 'createObjectURL', { value: () => 'blob:worker', configurable: true })
  const source = document.createElement('script')
  source.id = GENERATIVE_UI_WORKER_SOURCE_ELEMENT_ID
  source.textContent = '""'
  document.body.append(source)
  await import('../components/conversation/generativeUi/runner/generativeUiRunnerFrame')
  const port = { postMessage: vi.fn(), close: vi.fn(), addEventListener: vi.fn(), start: vi.fn() }
  const event = new MessageEvent('message', { data: { method: GENERATIVE_UI_CONNECT_METHOD, params: { protocolVersion: GENERATIVE_UI_PROTOCOL_VERSION } } })
  Object.defineProperty(event, 'source', { value: window.parent })
  Object.defineProperty(event, 'ports', { value: [port] })
  window.dispatchEvent(event)
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  document.body.replaceChildren()
})

function tick(each: (worker: FakeWorker) => void): void {
  for (let elapsed = 0; elapsed <= GENERATIVE_UI_LIMITS.workerUnresponsiveMs * 2; elapsed += GENERATIVE_UI_LIMITS.workerPingIntervalMs) {
    vi.advanceTimersByTime(GENERATIVE_UI_LIMITS.workerPingIntervalMs)
    each(FakeWorker.last)
  }
}

describe('generative UI runner frame watchdog', () => {
  it('keeps a worker that answers its pings', () => {
    tick(worker => worker.send({ type: 'pong', challenge: worker.pings.at(-1) }))
    expect(FakeWorker.last.terminated).toBe(false)
  })

  it('stops a worker that only sends unsolicited or replayed pongs', () => {
    let replayed: string | undefined
    tick(worker => {
      replayed ??= worker.pings[0]
      worker.send({ type: 'pong' })
      worker.send({ type: 'pong', challenge: replayed })
      worker.send({ type: 'snapshot' })
    })
    expect(FakeWorker.last.terminated).toBe(true)
  })
})
