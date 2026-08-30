import type {
  DesktopPluginSettingsMutation,
  DesktopPluginSettingsScope,
  DesktopPluginSettingsSnapshot
} from '@dotcraft/plugin'

type SettingsSnapshot = DesktopPluginSettingsSnapshot<Record<string, unknown>>
type SettingsListener = (settings: SettingsSnapshot) => void

interface PluginSettingsEntry {
  readonly listeners: Set<SettingsListener>
  delivered: string | null
  /** Prevents an older async read from publishing after a newer request. */
  issued: number
}

const PLUGIN_CONFIG_REGION = 'plugins.config'

const entries = new Map<string, PluginSettingsEntry>()
let stopWatching: (() => void) | null = null

export async function readDesktopPluginSettings(pluginId: string): Promise<SettingsSnapshot> {
  return await (window.api.appServer.sendRequestRaw('plugin/config/get', { id: pluginId }) as
    Promise<SettingsSnapshot>)
}

export async function mutateDesktopPluginSettings(
  pluginId: string,
  scope: DesktopPluginSettingsScope,
  operations: readonly DesktopPluginSettingsMutation[]
): Promise<SettingsSnapshot> {
  const snapshot = await (window.api.appServer.sendRequestRaw('plugin/config/mutate', {
    id: pluginId,
    scope,
    operations
  }) as Promise<SettingsSnapshot>)
  const entry = entries.get(pluginId)
  if (entry) {
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
  stopWatching = window.api.appServer.onNotificationRaw((notification) => {
    if (notification.method !== 'workspace/configChanged') return
    const regions = (notification.params as { regions?: unknown } | null | undefined)?.regions
    if (!Array.isArray(regions) || !regions.includes(PLUGIN_CONFIG_REGION)) return
    for (const [pluginId, entry] of entries) refresh(pluginId, entry)
  })
}

function refresh(pluginId: string, entry: PluginSettingsEntry): void {
  const issued = (entry.issued += 1)
  void readDesktopPluginSettings(pluginId).then((snapshot) => {
    if (entries.get(pluginId) !== entry || entry.issued !== issued) return
    publish(pluginId, snapshot)
  }, (readError) => console.error('Desktop Plugin settings re-read failed:', readError))
}

function publish(pluginId: string, snapshot: SettingsSnapshot): void {
  const entry = entries.get(pluginId)
  if (!entry) return
  const serialized = JSON.stringify(snapshot)
  if (serialized === entry.delivered) return
  entry.delivered = serialized
  for (const listener of [...entry.listeners]) {
    try {
      listener(snapshot)
    } catch (error) {
      console.error('Desktop Plugin settings listener failed:', error)
    }
  }
}
