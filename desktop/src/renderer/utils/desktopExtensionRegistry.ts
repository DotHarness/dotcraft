import type {
  PluginDesktopExtensionInfo,
  PluginDesktopExtensionSurface,
  PluginEntry
} from '../stores/pluginStore'
import type { ActiveMainView, ExtensionMainView } from '../stores/uiStore'

export interface DesktopMainViewExtension {
  viewKey: ExtensionMainView
  plugin: PluginEntry
  extension: PluginDesktopExtensionInfo
  surface: PluginDesktopExtensionSurface
  viewId: string
  label: string
  order: number
}

export function buildExtensionMainViewKey(
  pluginId: string,
  extensionId: string,
  viewId: string
): ExtensionMainView {
  return `extension:${encodePart(pluginId)}:${encodePart(extensionId)}:${encodePart(viewId)}`
}

export function parseExtensionMainViewKey(view: ActiveMainView): {
  pluginId: string
  extensionId: string
  viewId: string
} | null {
  if (!view.startsWith('extension:')) return null
  const parts = view.split(':')
  if (parts.length !== 4) return null
  return {
    pluginId: decodePart(parts[1]),
    extensionId: decodePart(parts[2]),
    viewId: decodePart(parts[3])
  }
}

export function isExtensionMainView(view: ActiveMainView): view is ExtensionMainView {
  return parseExtensionMainViewKey(view) !== null
}

export function getDesktopMainViewExtensions(plugins: PluginEntry[]): DesktopMainViewExtension[] {
  const result: DesktopMainViewExtension[] = []
  for (const plugin of plugins) {
    if (!plugin.installed || !plugin.enabled) continue
    for (const extension of plugin.desktopExtensions ?? []) {
      for (const surface of extension.surfaces ?? []) {
        if (surface.type !== 'mainView') continue
        const viewId = surface.viewId?.trim() || extension.id
        const label = surface.label?.trim() || extension.displayName || plugin.displayName
        result.push({
          viewKey: buildExtensionMainViewKey(plugin.id, extension.id, viewId),
          plugin,
          extension,
          surface,
          viewId,
          label,
          order: surface.order ?? 100
        })
      }
    }
  }
  return result.sort((a, b) => a.order - b.order || a.label.localeCompare(b.label))
}

export function findDesktopMainViewExtension(
  plugins: PluginEntry[],
  view: ActiveMainView
): DesktopMainViewExtension | null {
  const parsed = parseExtensionMainViewKey(view)
  if (!parsed) return null
  return getDesktopMainViewExtensions(plugins).find((entry) =>
    entry.plugin.id === parsed.pluginId
    && entry.extension.id === parsed.extensionId
    && entry.viewId === parsed.viewId
  ) ?? null
}

function encodePart(value: string): string {
  return encodeURIComponent(value)
}

function decodePart(value: string): string {
  return decodeURIComponent(value)
}
