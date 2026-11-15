import { PillSwitch, SettingsGroup, SettingsPanelShell, SettingsRow, Skeleton, Slider } from '@dotcraft/plugin'
import type { DesktopPluginViewProps } from '@dotcraft/plugin'
import { useState, type JSX } from 'react'
import type { ThemeAppearance } from '../../../shared/themes'
import { choiceAt, useChoices } from './choices'
import { useAppearance, useSettings } from './hooks'
import { stringsFor } from './i18n'
import { MIN_SURFACE_OPACITY, previewSettings, setSettings } from './settings'
import { cancelThemePreview, useThemeLibrary } from './themeLibrary'
import { ThemePicker } from './ThemePicker'

export function ThemeSettingsPage({ host }: DesktopPluginViewProps): JSX.Element | null {
  const settings = useSettings()
  const strings = stringsFor(host.environment.locale)
  const appearance = useAppearance(host)
  const [tab, setTab] = useState<ThemeAppearance>(appearance)
  useChoices()
  useThemeLibrary()

  if (!settings) {
    return (
      <SettingsPanelShell title={strings.settingsTitle} description={strings.settingsDescription}>
        <SettingsGroup>
          <div role="status" aria-busy="true" aria-label={strings.settingsTitle}>
            <SettingsRow>
              <Skeleton width="100%" height={18} />
            </SettingsRow>
            <SettingsRow>
              <Skeleton width="78%" height={18} />
            </SettingsRow>
          </div>
        </SettingsGroup>
      </SettingsPanelShell>
    )
  }
  const active = choiceAt({ kind: 'everywhere' }, tab) ?? { kind: 'none' as const }
  const opacity = Math.round(settings.surfaceOpacity * 100)

  return (
    <SettingsPanelShell title={strings.settingsTitle} description={strings.settingsDescription}>
      <SettingsGroup>
        <SettingsRow
          label={strings.enableLabel}
          description={strings.enableDescription}
          control={
            <PillSwitch
              checked={settings.enabled}
              aria-label={strings.enableLabel}
              onChange={(enabled) => {
                if (!enabled) cancelThemePreview()
                setSettings({ enabled })
              }}
            />
          }
        />
      </SettingsGroup>

      <ThemePicker
        host={host}
        appearance={tab}
        active={active}
        strings={strings}
        onAppearance={setTab}
      />

      <SettingsGroup title={strings.adjustGroup}>
        <SettingsRow
          label={strings.animateLabel}
          description={strings.animateDescription}
          control={
            <PillSwitch
              checked={settings.animate}
              aria-label={strings.animateLabel}
              onChange={(animate) => setSettings({ animate })}
            />
          }
        />
        <SettingsRow
          label={strings.blurLabel}
          control={
            <Slider
              min={0}
              max={64}
              value={settings.blur}
              ariaLabel={strings.blurLabel}
              valueText={`${settings.blur}px`}
              onValueChange={(blur) => previewSettings({ blur })}
              onValueCommit={(blur) => setSettings({ blur })}
            />
          }
        />
        <SettingsRow
          label={strings.surfaceLabel}
          description={strings.surfaceDescription}
          control={
            <Slider
              min={MIN_SURFACE_OPACITY * 100}
              max={100}
              value={opacity}
              ariaLabel={strings.surfaceLabel}
              valueText={`${opacity}%`}
              onValueChange={(value) => previewSettings({ surfaceOpacity: value / 100 })}
              onValueCommit={(value) => setSettings({ surfaceOpacity: value / 100 })}
            />
          }
        />
      </SettingsGroup>
    </SettingsPanelShell>
  )
}
