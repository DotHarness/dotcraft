import { expect, it, vi } from 'vitest'
vi.mock('electron', () => ({ BrowserWindow: { getAllWindows: () => [] } }))
import type { DesktopHubClient, HubEvent } from '../desktopHub'
import { getSatellitesHubBridge, disposeSatellitesHubBridge } from '../satellites/satellitesHubBridge'
import { getMobileHubBridge, disposeMobileHubBridge } from '../mobile/mobileHubBridge'

it('cancels application bridges and prevents their callbacks from resubscribing after shutdown', async () => {
  const signals: AbortSignal[] = []
  const client = { subscribeEvents: vi.fn((_event: (event: HubEvent) => void, signal: AbortSignal) => new Promise<void>((resolve) => { signals.push(signal); signal.addEventListener('abort', () => resolve(), { once: true }) })) } as unknown as DesktopHubClient
  const deps = { getHubClient: () => client }
  getSatellitesHubBridge(deps).acquire()
  getMobileHubBridge(deps).acquire()
  await Promise.resolve()
  expect(signals).toHaveLength(2)
  disposeSatellitesHubBridge()
  disposeMobileHubBridge()
  await Promise.resolve()
  expect(signals.every((signal) => signal.aborted)).toBe(true)
  getSatellitesHubBridge(deps).acquire()
  getMobileHubBridge(deps).acquire()
  expect(client.subscribeEvents).toHaveBeenCalledTimes(2)
})
