import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MCP_APP_SANDBOX_BRIDGE_VIOLATION_METHOD } from '../../shared/mcpAppSandbox'
import {
  GENERATIVE_UI_CONNECT_METHOD,
  GENERATIVE_UI_PROTOCOL_VERSION,
  GENERATIVE_UI_RUNNER_READY_METHOD,
  type GenerativeUiFailure
} from '../components/conversation/generativeUi/generativeUiProtocol'

type RunnerHost = typeof import('../components/conversation/generativeUi/generativeUiRunnerHost')

let host: RunnerHost

beforeEach(async () => {
  vi.useFakeTimers()
  vi.resetModules()
  host = await import('../components/conversation/generativeUi/generativeUiRunnerHost')
})

afterEach(() => {
  vi.useRealTimers()
  document.body.replaceChildren()
})

function frames(): HTMLIFrameElement[] {
  return [...document.querySelectorAll('iframe')]
}

function fromFrame(frame: HTMLIFrameElement, data: unknown): void {
  const event = new MessageEvent('message', { data })
  Object.defineProperty(event, 'source', { value: frame.contentWindow })
  window.dispatchEvent(event)
}

function lease(): { ports: MessagePort[]; failures: GenerativeUiFailure[]; release: () => void } {
  const ports: MessagePort[] = []
  const failures: GenerativeUiFailure[] = []
  const release = host.acquireGenerativeUiRunner({ onPort: port => ports.push(port), onFailure: failure => failures.push(failure) })
  return { ports, failures, release }
}

describe('shared Generative UI runner frame', () => {
  it('connects every block to its own port in one frame once the runner is ready', () => {
    host.prewarmGenerativeUiRunner()
    const first = lease()
    const second = lease()
    expect(frames()).toHaveLength(1)
    const post = vi.spyOn(frames()[0].contentWindow!, 'postMessage')
    expect(first.ports).toHaveLength(0)

    fromFrame(frames()[0], { method: GENERATIVE_UI_RUNNER_READY_METHOD, params: { protocolVersion: GENERATIVE_UI_PROTOCOL_VERSION } })
    const third = lease()

    expect([first.ports.length, second.ports.length, third.ports.length]).toEqual([1, 1, 1])
    const connects = post.mock.calls.filter(([message]) => (message as { method?: string }).method === GENERATIVE_UI_CONNECT_METHOD)
    expect(connects).toHaveLength(3)
    expect(connects.every(call => (call[2] as MessagePort[]).length === 1)).toBe(true)
    expect(frames()).toHaveLength(1)
  })

  it('fails every block when the sandbox closes the frame and starts a new frame for the next block', () => {
    const first = lease()
    const second = lease()
    const frame = frames()[0]
    fromFrame(frame, { method: GENERATIVE_UI_RUNNER_READY_METHOD, params: { protocolVersion: GENERATIVE_UI_PROTOCOL_VERSION } })

    fromFrame(frame, { method: MCP_APP_SANDBOX_BRIDGE_VIOLATION_METHOD, params: {} })

    expect([...first.failures, ...second.failures].map(failure => failure.stage)).toEqual(['host_error', 'host_error'])
    expect(frames()).toHaveLength(0)
    lease()
    expect(frames()).toHaveLength(1)
    expect(frames()[0]).not.toBe(frame)
  })

  it('times out blocks waiting for a frame that never becomes ready', () => {
    const waiting = lease()

    vi.advanceTimersByTime(10_000)

    expect(waiting.failures.map(failure => failure.stage)).toEqual(['frame_timeout'])
    expect(frames()).toHaveLength(0)
  })

  it('removes the frame a minute after the last block releases it', () => {
    const block = lease()
    fromFrame(frames()[0], { method: GENERATIVE_UI_RUNNER_READY_METHOD, params: { protocolVersion: GENERATIVE_UI_PROTOCOL_VERSION } })

    block.release()
    vi.advanceTimersByTime(59_000)
    expect(frames()).toHaveLength(1)
    lease().release()
    vi.advanceTimersByTime(59_000)
    expect(frames()).toHaveLength(1)
    vi.advanceTimersByTime(1_000)

    expect(frames()).toHaveLength(0)
  })
})
