import { ipcMain, type IpcMainInvokeEvent } from 'electron'
import { toMobileStatus, type MobileResult, type MobileStatusResult } from '../../shared/mobile'
import type { DesktopHubClient } from '../desktopHub'
import type { MobileHubBridge } from './mobileHubBridge'

type HandleSafe = (
  channel: string,
  listener: (event: IpcMainInvokeEvent, ...args: unknown[]) => unknown
) => void

export const MOBILE_CHANNELS = [
  'mobile:status',
  'mobile:enable',
  'mobile:disable',
  'mobile:create-pairing',
  'mobile:revoke',
  'mobile:set-relay',
  'mobile:clear-relay'
] as const

const SUBSCRIPTION_CHANNELS = ['mobile:subscribe', 'mobile:unsubscribe'] as const

export interface MobileIpcDeps {
  handleSafe: HandleSafe
  getHubClient: () => DesktopHubClient
  bridge: MobileHubBridge
}

function messageOf(reason: unknown): string {
  return reason instanceof Error ? reason.message : String(reason)
}

async function settle<T>(request: () => Promise<T>): Promise<MobileResult<T>> {
  try {
    return { ok: true, value: await request() }
  } catch (reason) {
    const code = (reason as { code?: string }).code ?? 'hubUnavailable'
    return { ok: false, failure: { code, message: messageOf(reason) } }
  }
}

const subscribedSenders = new Set<number>()

function releaseSender(bridge: MobileHubBridge, senderId: number): void {
  if (!subscribedSenders.delete(senderId)) return
  bridge.release()
}

export function registerMobileHandlers(deps: MobileIpcDeps): void {
  const { handleSafe, bridge } = deps
  for (const channel of SUBSCRIPTION_CHANNELS) ipcMain.removeAllListeners(channel)

  ipcMain.on('mobile:subscribe', (event) => {
    const senderId = event.sender.id
    if (subscribedSenders.has(senderId)) return
    subscribedSenders.add(senderId)
    bridge.acquire()
    event.sender.once('destroyed', () => releaseSender(bridge, senderId))
  })

  ipcMain.on('mobile:unsubscribe', (event) => releaseSender(bridge, event.sender.id))

  handleSafe('mobile:status', async (): Promise<MobileStatusResult> => {
    const hubClient = deps.getHubClient()
    const [status, listing] = await Promise.allSettled([hubClient.getStatus(), hubClient.getMobile()])

    if (listing.status === 'fulfilled') return { supported: true, status: toMobileStatus(listing.value) }

    const declared = status.status === 'fulfilled' && status.value.capabilities?.mobile === true
    if (!declared) return { supported: false, status: null }
    return { supported: true, status: null, error: messageOf(listing.reason) }
  })

  handleSafe('mobile:enable', () =>
    settle(async () => toMobileStatus(await deps.getHubClient().enableMobile()))
  )

  handleSafe('mobile:disable', () =>
    settle(async () => toMobileStatus(await deps.getHubClient().disableMobile()))
  )

  handleSafe('mobile:create-pairing', () => settle(() => deps.getHubClient().createMobilePairing()))

  handleSafe('mobile:revoke', (_event, input) =>
    settle(() => deps.getHubClient().revokeMobileDevice((input as { deviceId: string }).deviceId))
  )

  handleSafe('mobile:set-relay', (_event, input) => {
    const { url, token } = input as { url: string; token: string }
    return settle(async () => toMobileStatus(await deps.getHubClient().setMobileRelay(url, token)))
  })

  handleSafe('mobile:clear-relay', () =>
    settle(async () => toMobileStatus(await deps.getHubClient().clearMobileRelay()))
  )
}
