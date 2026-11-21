import type { DesktopPluginSettingsMutation } from '@dotcraft/plugin'
import type { AppLocale } from '../../../shared/locales'
import type { ChannelPluginInfo } from '../../../shared/channelPlugin'
import type { PluginEntry } from '../../stores/pluginStore'
import type { PluginSettingsSnapshot } from '../../plugins/desktopPluginSettings'
import type { ChannelConnectionState } from './ChannelCard'

export type ChannelPlugin = PluginEntry & { channel: ChannelPluginInfo }

export function isChannelPlugin(entry: PluginEntry): entry is ChannelPlugin {
  return entry.channel != null
}

export function isChannelOnlyPlugin(entry: PluginEntry): boolean {
  return isChannelPlugin(entry) && !entry.dotnet && !entry.desktop &&
    !(entry.apps?.length || entry.skills.length || entry.functions.length || entry.hooks?.length ||
      entry.mcpServers.length || entry.lspServers.length || entry.workflows?.length)
}

export function groupChannelPlugins(plugins: PluginEntry[], statuses: ReadonlyMap<string, ChannelStatus> | null): ChannelPlugin[][] {
  const groups = new Map<string, ChannelPlugin[]>()
  for (const plugin of plugins) {
    if (!isChannelPlugin(plugin)) continue
    const name = normalizeChannelName(plugin.channel.channelName)
    const group = groups.get(name) ?? []
    group.push(plugin)
    groups.set(name, group)
  }
  const priority = (plugin: ChannelPlugin, active: string | undefined): number =>
    plugin.installed && active === plugin.id ? 3 : plugin.installed ? 2 : plugin.installable ? 1 : 0
  return [...groups].map(([name, group]) => group.sort((a, b) =>
    priority(b, statuses?.get(name)?.pluginId) - priority(a, statuses?.get(name)?.pluginId)))
}

export function normalizeChannelName(name: string): string {
  return name.trim().toLowerCase()
}

export function channelTitle(plugin: ChannelPlugin, locale: AppLocale): string {
  return plugin.channel.localizedDisplayName?.[locale] ?? plugin.channel.displayName
}

export function channelSubtitle(plugin: ChannelPlugin, locale: AppLocale): string {
  return plugin.channel.interface?.localizedShortDescription?.[locale]
    ?? plugin.channel.interface?.shortDescription
    ?? plugin.description
    ?? ''
}

export function channelLogoPath(plugin: ChannelPlugin): string | undefined {
  const channel = normalizeChannelName(plugin.channel.channelName)
  switch (channel) {
    case 'feishu': return new URL('../../assets/channels/feishu.svg', import.meta.url).href
    case 'qq': return new URL('../../assets/channels/qq.svg', import.meta.url).href
    case 'telegram': return new URL('../../assets/channels/telegram.svg', import.meta.url).href
    case 'wecom': return new URL('../../assets/channels/wecom.svg', import.meta.url).href
    case 'weixin': return new URL('../../assets/channels/weixin.svg', import.meta.url).href
    default: return plugin.interface?.logoDataUrl ?? undefined
  }
}

export interface ChannelStatus {
  name: string
  pluginId?: string
  enabled: boolean
  running: boolean
  runtimeState?: string
  failureCode?: string
}

export function deriveChannelStatus(
  status: ChannelStatus | undefined,
  configured: boolean
): ChannelConnectionState {
  if (status?.runtimeState === 'failed') return 'error'
  if (status?.running) return 'connected'
  if (status?.runtimeState === 'starting') return 'connecting'
  if (status?.enabled) return 'enabledNotConnected'
  return configured ? 'stopped' : 'notConfigured'
}

export const channelStatusLabels: Record<ChannelConnectionState, string> = {
  connected: 'channels.status.connected',
  notConfigured: 'channels.status.notConfigured',
  enabledNotConnected: 'channels.status.enabledNotConnected',
  connecting: 'channels.modules.connecting',
  stopped: 'channels.modules.stopped',
  error: 'channels.modules.error'
}

export function missingChannelSettings(
  channel: ChannelPluginInfo,
  snapshot: PluginSettingsSnapshot
): string[] {
  return (channel.configDescriptors ?? []).flatMap((field) => {
    const { key } = field
    if (!field.required || field.interactiveSetupOnly || key.startsWith('dotcraft.')) return []
    if (field.masked || field.dataKind === 'secret') {
      return snapshot.configuredSecrets?.includes(key) ? [] : [key]
    }
    const value = Object.hasOwn(snapshot.value, key) ? snapshot.value[key] : undefined
    const missing = value == null || (typeof value === 'string' && value.trim() === '') ||
      (Array.isArray(value) && value.length === 0)
    return missing ? [key] : []
  })
}

export function channelSettingOperations(
  snapshot: PluginSettingsSnapshot,
  draft: Record<string, unknown>
): DesktopPluginSettingsMutation[] {
  const operations: DesktopPluginSettingsMutation[] = []
  for (const [key, value] of Object.entries(draft)) {
    const stored = Object.hasOwn(snapshot.workspace, key)
    if (value === undefined) {
      if (stored || snapshot.configuredSecrets?.includes(key)) operations.push({ op: 'unset', key })
    } else if (!stored || JSON.stringify(value) !== JSON.stringify(snapshot.workspace[key])) {
      operations.push({ op: 'set', key, value })
    }
  }
  return operations
}
