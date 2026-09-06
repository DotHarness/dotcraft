import { ipcMain, type IpcMainInvokeEvent } from 'electron'
import {
  normalizeSatelliteInvite,
  normalizeSatellites,
  type SatelliteEvent,
  type SatelliteInvite,
  type SatelliteListResult,
  type SharePcStatus
} from '../../shared/satellites'
import type { DesktopHubClient } from '../desktopHub'
import type { AppSettings, CreatedSatelliteInvite } from '../settings'
import { readSharePcStatus } from './satelliteRuntime'
import type { SatellitesHubBridge } from './satellitesHubBridge'

/**
 * The renderer reaches Hub only through here, so the Hub bearer token and any
 * credential reference stay in the main process.
 */

type HandleSafe = (
  channel: string,
  listener: (event: IpcMainInvokeEvent, ...args: unknown[]) => unknown
) => void

/** All invoke channels this module owns, used for teardown in `unregisterIpcHandlers`. */
export const SATELLITES_CHANNELS = [
  'satellites:list',
  'satellites:create-invite',
  'satellites:revoke',
  'satellites:activity',
  'satellites:share-status'
] as const

const SUBSCRIPTION_CHANNELS = ['satellites:subscribe', 'satellites:unsubscribe'] as const

export interface SatellitesIpcDeps {
  handleSafe: HandleSafe
  getHubClient: () => DesktopHubClient
  bridge: SatellitesHubBridge
  getSettings: () => AppSettings
  updateSettings: (partial: Partial<AppSettings>) => void | Promise<void>
}

function asObject(value: unknown): Record<string, unknown> {
  return value != null && typeof value === 'object' ? (value as Record<string, unknown>) : {}
}

function optionalText(value: unknown): string | undefined {
  if (typeof value !== 'string') return undefined
  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed : undefined
}

function messageOf(reason: unknown): string {
  return reason instanceof Error ? reason.message : String(reason)
}

const subscribedSenders = new Set<number>()

function releaseSender(bridge: SatellitesHubBridge, senderId: number): void {
  if (!subscribedSenders.delete(senderId)) return
  bridge.release()
}

export function registerSatellitesHandlers(deps: SatellitesIpcDeps): void {
  const { handleSafe, bridge } = deps
  // Subscriptions are re-attached, not reset: the bridge outlives a workspace switch.
  for (const channel of SUBSCRIPTION_CHANNELS) ipcMain.removeAllListeners(channel)

  ipcMain.on('satellites:subscribe', (event) => {
    const senderId = event.sender.id
    if (subscribedSenders.has(senderId)) return
    subscribedSenders.add(senderId)
    bridge.acquire()
    event.sender.once('destroyed', () => releaseSender(bridge, senderId))
  })

  ipcMain.on('satellites:unsubscribe', (event) => releaseSender(bridge, event.sender.id))

  handleSafe('satellites:list', async (): Promise<SatelliteListResult> => {
    const hubClient = deps.getHubClient()
    const [status, listing] = await Promise.allSettled([
      hubClient.getStatus(),
      hubClient.listSatellites()
    ])

    if (listing.status === 'fulfilled') {
      const satellites = normalizeSatellites(listing.value)
      bridge.remember(satellites)
      return { supported: true, satellites }
    }

    const declared = status.status === 'fulfilled' && status.value.capabilities?.satellites === true
    // An old Hub has no satellite surface at all; that is a setup state, not an error.
    if (!declared) return { supported: false, satellites: [] }
    return { supported: true, satellites: [], error: messageOf(listing.reason) }
  })

  handleSafe('satellites:create-invite', async (_event, input): Promise<SatelliteInvite> => {
    const raw = asObject(input)
    const ttlHours = typeof raw.ttlHours === 'number' && Number.isFinite(raw.ttlHours)
      ? Math.trunc(raw.ttlHours)
      : undefined
    const purpose = optionalText(raw.purpose)
    const minted = await deps.getHubClient().createSatelliteInvite({
      ...(optionalText(raw.name) ? { name: optionalText(raw.name) as string } : {}),
      ...(optionalText(raw.host) ? { host: optionalText(raw.host) as string } : {}),
      ...(purpose ? { purpose } : {}),
      ...(ttlHours != null ? { ttlHours } : {})
    })

    // Only the invitation's own fields cross to the renderer; the Hub token never does.
    const invite = normalizeSatelliteInvite(minted, purpose)
    if (!invite) throw new Error('Hub returned an unusable invitation.')
    await rememberInvite(deps, invite)
    return invite
  })

  handleSafe('satellites:revoke', async (_event, input) => {
    const { peerId } = asObject(input) as { peerId?: string }
    const id = optionalText(peerId)
    if (!id) throw new Error('A machine is required.')
    await deps.getHubClient().revokeSatellite(id)
    return { ok: true }
  })

  handleSafe('satellites:activity', (_event, input): SatelliteEvent[] => {
    const { peerId } = asObject(input) as { peerId?: string }
    return bridge.recentActivity(optionalText(peerId))
  })

  handleSafe('satellites:share-status', (): Promise<SharePcStatus> => readSharePcStatus())
}

/**
 * Remembers the invitation id so a machine joining through it can be announced. Only
 * the id and its expiry are stored; the invitation URL never leaves memory.
 */
async function rememberInvite(deps: SatellitesIpcDeps, invite: SatelliteInvite): Promise<void> {
  const existing = deps.getSettings().createdSatelliteInviteIds ?? []
  const kept: CreatedSatelliteInvite[] = existing.filter(
    (entry) => entry.inviteId !== invite.inviteId
  )
  kept.push({ inviteId: invite.inviteId, expiresAt: invite.expiresAt })
  await deps.updateSettings({ createdSatelliteInviteIds: kept })
}
