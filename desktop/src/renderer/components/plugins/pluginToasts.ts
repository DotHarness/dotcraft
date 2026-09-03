import type { PluginEntry } from '../../stores/pluginStore'
import { showToast, type ToastLeading } from '../../stores/toastStore'
import { pluginTitle } from './PluginCatalogItem'
import { stagePluginTryInChat } from './pluginDraft'

export function pluginToastLeading(plugin: PluginEntry): ToastLeading {
  const src = plugin.interface?.composerIconDataUrl || plugin.interface?.logoDataUrl
  return { ...(src ? { src } : {}), fallback: pluginTitle(plugin).slice(0, 1) }
}

export function showPluginInstalledToast(
  plugin: PluginEntry,
  labels: { message: string; tryLabel?: string }
): void {
  showToast({
    message: labels.message,
    key: `plugin-lifecycle:${plugin.id}`,
    leading: pluginToastLeading(plugin),
    ...(labels.tryLabel ? { action: { label: labels.tryLabel, onClick: () => stagePluginTryInChat(plugin) } } : {})
  })
}

export function showPluginUninstalledToast(plugin: PluginEntry, message: string): void {
  showToast({ message, type: 'success', key: `plugin-lifecycle:${plugin.id}` })
}
