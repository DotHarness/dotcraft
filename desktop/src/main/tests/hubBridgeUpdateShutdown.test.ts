import { expect, it, vi } from 'vitest'

vi.mock('electron', () => ({
  BrowserWindow: { getAllWindows: () => [] }
}))

import type { DesktopHubClient, HubEvent } from '../desktopHub'
import { AppUpdateRuntime } from '../appUpdateRuntime'
import { getSatellitesHubBridge, disposeSatellitesHubBridge } from '../satellites/satellitesHubBridge'
import { getMobileHubBridge, disposeMobileHubBridge } from '../mobile/mobileHubBridge'

it('closes both process-wide streams before waiting for update runtime idle', async () => {
  const runtime = new AppUpdateRuntime()
  const signals: AbortSignal[] = []
  const client = {
    subscribeEvents: vi.fn((_onEvent: (event: HubEvent) => void, signal: AbortSignal) =>
      runtime.run(() => new Promise<void>((resolve) => {
        signals.push(signal)
        signal.addEventListener('abort', () => resolve(), { once: true })
      }))
    )
  } as unknown as DesktopHubClient
  const deps = { getHubClient: () => client }
  const satellites = getSatellitesHubBridge(deps)
  const mobile = getMobileHubBridge(deps)
  satellites.acquire()
  satellites.acquire()
  mobile.acquire()
  mobile.acquire()
  await Promise.resolve()
  expect(signals).toHaveLength(2)

  runtime.quiesce()
  let idle = false
  const waiting = runtime.waitForIdle(new AbortController().signal).then(() => { idle = true })
  await Promise.resolve()
  expect(idle).toBe(false)

  disposeSatellitesHubBridge()
  disposeMobileHubBridge()
  await waiting
  expect(signals.every((signal) => signal.aborted)).toBe(true)

  disposeSatellitesHubBridge()
  disposeMobileHubBridge()
  getSatellitesHubBridge(deps).acquire()
  getMobileHubBridge(deps).acquire()
  expect(client.subscribeEvents).toHaveBeenCalledTimes(2)
})
