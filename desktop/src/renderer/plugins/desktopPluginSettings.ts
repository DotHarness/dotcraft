import { useConnectionStore } from '../stores/connectionStore'
import type {
  DesktopPluginSettingsMutation,
  DesktopPluginSettingsScope,
  DesktopPluginSettingsSnapshot
} from '@dotcraft/plugin'

export type PluginSettingsSnapshot = DesktopPluginSettingsSnapshot<Record<string, unknown>> & {
  configuredSecrets?: readonly string[]
}
type SettingsListener = (settings: PluginSettingsSnapshot) => void

interface PluginSettingsEntry {
  readonly listeners: Set<SettingsListener>
  delivered: string | null
  /** Prevents an older async read from publishing after a newer request. */
  issued: number
}

const PLUGIN_CONFIG_REGION = 'plugins.config'

const entries = new Map<string, PluginSettingsEntry>()
let stopWatching: (() => void) | null = null

function requestSettings(method: 'plugin/config/get' | 'plugin/config/mutate', params: unknown): Promise<PluginSettingsSnapshot> {
  const { connectionId } = useConnectionStore.getState()
  const requests = window.api.appServer
  return (connectionId
    ? requests.sendRequestRaw(method, params, undefined, { connectionId })
    : requests.sendRequestRaw(method, params)) as Promise<PluginSettingsSnapshot>
}

export function readDesktopPluginSettings(pluginId: string): Promise<PluginSettingsSnapshot> {
  return requestSettings('plugin/config/get', { id: pluginId })
}

export async function mutateDesktopPluginSettings(
  pluginId: string,
  scope: DesktopPluginSettingsScope,
  operations: readonly DesktopPluginSettingsMutation[]
): Promise<PluginSettingsSnapshot> {
  const connectionEpoch = useConnectionStore.getState().connectionEpoch
  const snapshot = await requestSettings('plugin/config/mutate', { id: pluginId, scope, operations })
  const entry = entries.get(pluginId)
  if (entry && useConnectionStore.getState().connectionEpoch === connectionEpoch) {
    entry.issued += 1
    publish(pluginId, snapshot)
  }
  return snapshot
}

export function onDesktopPluginSettingsChange(
  pluginId: string,
  listener: SettingsListener
): () => void {
  const entry = entries.get(pluginId) ?? createEntry(pluginId)
  entry.listeners.add(listener)
  startWatching()
  return () => {
    if (!entry.listeners.delete(listener) || entry.listeners.size > 0) return
    entries.delete(pluginId)
    if (entries.size > 0) return
    stopWatching?.()
    stopWatching = null
  }
}

function createEntry(pluginId: string): PluginSettingsEntry {
  const entry: PluginSettingsEntry = { listeners: new Set(), delivered: null, issued: 0 }
  entries.set(pluginId, entry)
  void readDesktopPluginSettings(pluginId).then((snapshot) => {
    if (entries.get(pluginId) === entry && entry.delivered === null) {
      entry.delivered = JSON.stringify(snapshot)
    }
  }, () => {})
  return entry
}

function startWatching(): void {
  if (stopWatching) return
  const stopNotifications = window.api.appServer.onNotificationRaw((notification) => {
    if (notification.method !== 'config/changed') return
    const regions = (notification.params as { regions?: unknown } | null | undefined)?.regions
    if (!Array.isArray(regions) || !regions.includes(PLUGIN_CONFIG_REGION)) return
    for (const [pluginId, entry] of entries) refresh(pluginId, entry)
  })
  const stopConnection = useConnectionStore.subscribe((next, previous) => {
    if (next.connectionEpoch === previous.connectionEpoch) return
    for (const [pluginId, entry] of entries) {
      entry.issued += 1
      if (next.status === 'connected') refresh(pluginId, entry)
    }
  })
  stopWatching = () => {
    stopNotifications()
    stopConnection()
  }
}

function refresh(pluginId: string, entry: PluginSettingsEntry): void {
  const issued = (entry.issued += 1)
  void readDesktopPluginSettings(pluginId).then((snapshot) => {
    if (entries.get(pluginId) !== entry || entry.issued !== issued) return
    publish(pluginId, snapshot)
  }, (readError) => console.error('Desktop extension settings re-read failed:', readError))
}

function publish(pluginId: string, snapshot: PluginSettingsSnapshot): void {
  const entry = entries.get(pluginId)
  if (!entry) return
  const serialized = JSON.stringify(snapshot)
  if (serialized === entry.delivered) return
  entry.delivered = serialized
  for (const listener of [...entry.listeners]) {
    try {
      listener(snapshot)
    } catch (error) {
      console.error('Desktop extension settings listener failed:', error)
    }
  }
}
