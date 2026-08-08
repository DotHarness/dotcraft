import { useEffect, useMemo, useState } from 'react'
import type { DesktopMainViewExtension, DesktopSettingsPanelExtension } from '../../utils/desktopExtensionRegistry'
import { buildExtensionMainViewKey } from '../../utils/desktopExtensionRegistry'
import { useUIStore } from '../../stores/uiStore'
import { useThreadStore } from '../../stores/threadStore'
import {
  authorizeAndLoadActivation,
  createDesktopExtensionHost,
  type ExtensionComponent
} from './DesktopExtensionMainView'

export function DesktopExtensionSettingsPanel({ entry }: { entry: DesktopSettingsPanelExtension }): JSX.Element {
  const [component, setComponent] = useState<ExtensionComponent | null>(null)
  const [grantId, setGrantId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const setActiveMainView = useUIStore((state) => state.setActiveMainView)
  const setActiveThreadId = useThreadStore((state) => state.setActiveThreadId)
  const mainLikeEntry = useMemo<DesktopMainViewExtension>(() => ({
    viewKey: buildExtensionMainViewKey(entry.plugin.id, entry.extension.id, entry.settingsId),
    plugin: entry.plugin,
    extension: entry.extension,
    surface: entry.surface,
    viewId: entry.settingsId,
    label: entry.label,
    localizedLabel: entry.localizedLabel,
    icon: entry.icon,
    order: entry.order
  }), [entry])
  const host = useMemo(() => grantId
    ? createDesktopExtensionHost(mainLikeEntry, grantId, setActiveMainView, setActiveThreadId)
    : null, [grantId, mainLikeEntry, setActiveMainView, setActiveThreadId])

  useEffect(() => {
    let cancelled = false
    let activeGrantId: string | null = null
    setComponent(null)
    setGrantId(null)
    setError(null)
    authorizeAndLoadActivation(mainLikeEntry, setActiveMainView, setActiveThreadId)
      .then(({ activation, grantId: nextGrantId }) => {
        activeGrantId = nextGrantId
        if (cancelled) {
          void window.api.desktopExtensions.revokeExtension({ grantId: nextGrantId })
          return
        }
        const next = activation.surfaces?.settingsPanels?.[entry.settingsId]
          ?? activation.settingsPanels?.[entry.settingsId]
          ?? null
        if (!next) {
          setError(`Desktop extension '${entry.extension.displayName}' did not provide settings panel '${entry.settingsId}'.`)
          void window.api.desktopExtensions.revokeExtension({ grantId: nextGrantId })
          activeGrantId = null
          return
        }
        setGrantId(nextGrantId)
        setComponent(() => next)
      })
      .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : String(reason)))
    return () => {
      cancelled = true
      if (activeGrantId) void window.api.desktopExtensions.revokeExtension({ grantId: activeGrantId })
    }
  }, [entry, mainLikeEntry, setActiveMainView, setActiveThreadId])

  if (error) return <div role="alert" className="settings-inline-error">{error}</div>
  if (!component || !host) return <div role="status">Loading {entry.label} settings…</div>
  const Component = component
  return <Component host={host} viewId={entry.settingsId} />
}
