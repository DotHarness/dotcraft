import type { PluginEntry } from '../stores/pluginStore'

interface PluginDesktopExtensionContent {
  key: string
  title: string
  kind: string
  description: string
}

type Translate = (key: string, vars?: Record<string, string | number>) => string

export function getPluginDesktopExtensionContents(
  plugin: PluginEntry,
  t: Translate
): PluginDesktopExtensionContent[] {
  return (plugin.desktopExtensions ?? []).flatMap((extension) => {
    const pluginDetailSurfaces = extension.surfaces.filter((surface) => surface.type === 'pluginDetail')
    if (pluginDetailSurfaces.length === 0) {
      return [{
        key: `desktop-extension:${extension.id}`,
        title: extension.displayName,
        kind: t('plugins.content.desktopExtension'),
        description: extension.description ?? ''
      }]
    }

    return pluginDetailSurfaces.map((surface, index) => ({
      key: `desktop-extension:${extension.id}:${surface.slot ?? index}`,
      title: surface.title ?? extension.displayName,
      kind: t('plugins.content.desktopExtension'),
      description: surface.description ?? extension.description ?? ''
    }))
  })
}
