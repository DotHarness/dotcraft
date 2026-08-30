import { PillSwitch, SettingsGroup, SettingsPanelShell, SettingsRow, Skeleton, Slider } from '@dotcraft/plugin'
import type { DesktopPluginViewProps } from '@dotcraft/plugin'
import { useEffect, useRef, useSyncExternalStore, type JSX } from 'react'
import { stringsFor } from './i18n'
import { getSettings, previewSettings, setSettings, subscribeSettings, type TokenHudSettings } from './settings'

function useSettings(): ReturnType<typeof getSettings> {
  return useSyncExternalStore(subscribeSettings, getSettings, getSettings)
}

export function TokenHudSettingsPage({ host }: DesktopPluginViewProps): JSX.Element | null {
  const settings = useSettings()
  const strings = stringsFor(host.environment.locale)
  const previewFrame = useRef<number | null>(null)
  const pendingPreview = useRef<Partial<TokenHudSettings>>({})

  useEffect(() => () => {
    if (previewFrame.current !== null) cancelAnimationFrame(previewFrame.current)
  }, [])

  const preview = (patch: Partial<TokenHudSettings>): void => {
    pendingPreview.current = { ...pendingPreview.current, ...patch }
    if (previewFrame.current !== null) return
    previewFrame.current = requestAnimationFrame(() => {
      previewFrame.current = null
      const next = pendingPreview.current
      pendingPreview.current = {}
      previewSettings(next)
    })
  }

  const commit = (patch: Partial<TokenHudSettings>): void => {
    if (previewFrame.current !== null) {
      cancelAnimationFrame(previewFrame.current)
      previewFrame.current = null
    }
    pendingPreview.current = {}
    previewSettings(patch)
    setSettings(patch)
  }

  if (!settings) {
    return (
      <SettingsPanelShell title={strings.settingsTitle} description={strings.settingsDescription}>
        <SettingsGroup title={strings.generalGroup}>
          <div role="status" aria-busy="true" aria-label={strings.settingsTitle}>
            <SettingsRow><Skeleton width="100%" height={18} /></SettingsRow>
            <SettingsRow><Skeleton width="72%" height={18} /></SettingsRow>
          </div>
        </SettingsGroup>
      </SettingsPanelShell>
    )
  }

  return (
    <SettingsPanelShell title={strings.settingsTitle} description={strings.settingsDescription}>
      <SettingsGroup title={strings.generalGroup}>
        <SettingsRow
          label={strings.visibleLabel}
          description={strings.visibleDescription}
          control={
            <PillSwitch
              checked={settings.visible}
              aria-label={strings.visibleLabel}
              onChange={(visible) => setSettings({ visible })}
            />
          }
        />
        <SettingsRow
          label={strings.opacityLabel}
          control={
            <Slider
              min={20}
              max={100}
              value={settings.opacity}
              ariaLabel={strings.opacityLabel}
              valueText={`${settings.opacity}%`}
              onValueChange={(opacity) => preview({ opacity })}
              onValueCommit={(opacity) => commit({ opacity })}
            />
          }
        />
      </SettingsGroup>
    </SettingsPanelShell>
  )
}
