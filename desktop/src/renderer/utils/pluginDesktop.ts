import type { PluginEntry } from '../stores/pluginStore'

interface PluginDesktopContent {
  key: string
  title: string
  kind: string
  description: string
}

type Translate = (key: string, vars?: Record<string, string | number>) => string

export function getPluginDesktopContent(
  plugin: PluginEntry,
  t: Translate
): PluginDesktopContent[] {
  if (!plugin.desktop) return []
  return [{
    key: 'desktop-plugin',
    title: plugin.interface?.displayName || plugin.displayName,
    kind: t('plugins.content.desktopPlugin'),
    description: plugin.interface?.shortDescription || plugin.description || ''
  }]
}
