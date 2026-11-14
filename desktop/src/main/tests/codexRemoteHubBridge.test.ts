import { EventEmitter } from 'node:events'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import type { WebContents } from 'electron'
import type { HubEvent, DesktopHubClient } from '../desktopHub'
import type { CodexRemoteStatus } from '../../shared/codexRemote'

const ipc = vi.hoisted(() => ({ on: vi.fn(), removeListener: vi.fn() }))
vi.mock('electron', () => ({ ipcMain: ipc }))
import { CodexRemoteHubBridge } from '../codexRemote/codexRemoteHubBridge'

const ON: CodexRemoteStatus = { state: 'on', signedIn: true, hostName: 'Test host' }

function sender(): EventEmitter & { send: ReturnType<typeof vi.fn>; isDestroyed: () => boolean } {
  return Object.assign(new EventEmitter(), { send: vi.fn(), isDestroyed: () => false })
}

function harness() {
  const streams: Array<{ event: (event: HubEvent) => void; signal: AbortSignal; end: () => void; connected: () => void }> = []
  const client = {
    getCodexRemote: vi.fn(async () => ON),
    subscribeEvents: vi.fn((event: (event: HubEvent) => void, signal: AbortSignal, connected: () => void) =>
      new Promise<void>((resolve) => {
        streams.push({ event, signal, end: resolve, connected })
        signal.addEventListener('abort', () => resolve(), { once: true })
      }))
  }
  const bridge = new CodexRemoteHubBridge(() => client as unknown as DesktopHubClient)
  return { bridge, client, streams }
}

beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())

it('shares a stream only with subscribed windows and releases it on unsubscribe or destruction', async () => {
  const { bridge, client, streams } = harness()
  const first = sender()
  const second = sender()
  bridge.acquire(first as unknown as WebContents)
  bridge.acquire(first as unknown as WebContents)
  bridge.acquire(second as unknown as WebContents)
  expect(client.subscribeEvents).toHaveBeenCalledTimes(1)
  streams[0].event({ kind: 'codexRemote.paired', at: 'now' })
  expect(first.send).toHaveBeenCalledWith('codexRemote:event', { kind: 'paired' })
  expect(second.send).toHaveBeenCalledTimes(1)

  bridge.release(first as unknown as WebContents)
  streams[0].event({ kind: 'codexRemote.stateChanged', at: 'now', data: ON })
  expect(first.send).toHaveBeenCalledTimes(1)
  second.emit('destroyed')
  expect(streams[0].signal.aborted).toBe(true)
  expect(first.listenerCount('destroyed')).toBe(0)
  expect(second.listenerCount('destroyed')).toBe(0)
  await vi.advanceTimersByTimeAsync(5000)
  expect(client.subscribeEvents).toHaveBeenCalledTimes(1)
})

it('refreshes after reconnect, keeps newer events over an old snapshot, and drops in-flight work after dispose', async () => {
  const { bridge, client, streams } = harness()
  const window = sender()
  bridge.acquire(window as unknown as WebContents)
  streams[0].end()
  await vi.advanceTimersByTimeAsync(1000)
  expect(streams).toHaveLength(2)
  let resolveStatus!: (status: CodexRemoteStatus) => void
  client.getCodexRemote.mockImplementation(() => new Promise((resolve) => { resolveStatus = resolve }))
  streams[1].connected()
  streams[1].event({ kind: 'codexRemote.stateChanged', at: 'now', data: { ...ON, state: 'off' } })
  resolveStatus(ON)
  await Promise.resolve()
  expect(window.send).toHaveBeenCalledTimes(1)
  expect(window.send).toHaveBeenLastCalledWith('codexRemote:event', { kind: 'stateChanged', status: { ...ON, state: 'off' } })

  streams[1].end()
  await vi.advanceTimersByTimeAsync(1000)
  client.getCodexRemote.mockResolvedValue(ON)
  streams[2].connected()
  await Promise.resolve()
  expect(window.send).toHaveBeenLastCalledWith('codexRemote:event', { kind: 'stateChanged', status: ON })

  client.getCodexRemote.mockImplementation(() => new Promise((resolve) => { resolveStatus = resolve }))
  streams[2].connected()
  bridge.dispose()
  resolveStatus({ ...ON, state: 'off' })
  await vi.advanceTimersByTimeAsync(5000)
  expect(window.send).toHaveBeenCalledTimes(2)
  expect(streams[2].signal.aborted).toBe(true)
  expect(client.subscribeEvents).toHaveBeenCalledTimes(3)
})
