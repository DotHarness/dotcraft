import type {
  PluginDesktopExtensionInfo,
  PluginDesktopExtensionSurface,
  PluginEntry
} from '../stores/pluginStore'
import type { ActiveMainView, ExtensionMainView } from '../stores/uiStore'
import type { LocalizedTextMap } from '../../shared/locales'

export interface DesktopMainViewExtension {
  viewKey: ExtensionMainView
  plugin: PluginEntry
  extension: PluginDesktopExtensionInfo
  surface: PluginDesktopExtensionSurface
  viewId: string
  /** Base (English) label; resolve per-locale display with `localizedLabel`. */
  label: string
  /** Optional per-locale overrides for `label`, resolved by the host at render. */
  localizedLabel: LocalizedTextMap | null
  icon: string | null
  order: number
}

export interface DesktopSettingsPanelExtension {
  settingsKey: `extension-settings:${string}:${string}:${string}`
  plugin: PluginEntry
  extension: PluginDesktopExtensionInfo
  surface: PluginDesktopExtensionSurface
  settingsId: string
  label: string
  localizedLabel: LocalizedTextMap | null
  icon: string | null
  order: number
}

export function buildExtensionSettingsPanelKey(
  pluginId: string,
  extensionId: string,
  settingsId: string
): DesktopSettingsPanelExtension['settingsKey'] {
  return `extension-settings:${encodePart(pluginId)}:${encodePart(extensionId)}:${encodePart(settingsId)}`
}

export function getDesktopSettingsPanelExtensions(plugins: PluginEntry[]): DesktopSettingsPanelExtension[] {
  const result: DesktopSettingsPanelExtension[] = []
  for (const plugin of plugins) {
    if (!plugin.installed || !plugin.enabled) continue
    for (const extension of plugin.desktopExtensions ?? []) {
      for (const surface of extension.surfaces ?? []) {
        if (surface.type !== 'settingsPanel') continue
        const settingsId = surface.settingsId?.trim() || extension.id
        const label = surface.label?.trim() || extension.displayName || plugin.displayName
        result.push({
          settingsKey: buildExtensionSettingsPanelKey(plugin.id, extension.id, settingsId),
          plugin,
          extension,
          surface,
          settingsId,
          label,
          localizedLabel: surface.localizedLabel ?? null,
          icon: surface.icon?.trim() || null,
          order: surface.order ?? 100
        })
      }
    }
  }
  return result.sort((a, b) => a.order - b.order || a.label.localeCompare(b.label))
}

export function findDesktopSettingsPanelExtension(
  plugins: PluginEntry[],
  settingsKey: string
): DesktopSettingsPanelExtension | null {
  return getDesktopSettingsPanelExtensions(plugins).find((entry) => entry.settingsKey === settingsKey) ?? null
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
        const localizedLabel = surface.localizedLabel ?? null
        const icon = surface.icon?.trim() || null
        result.push({
          viewKey: buildExtensionMainViewKey(plugin.id, extension.id, viewId),
          plugin,
          extension,
          surface,
          viewId,
          label,
          localizedLabel,
          icon,
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
