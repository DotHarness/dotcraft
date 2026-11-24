import { PillSwitch, SettingsBreadcrumb, SettingsGroup, SettingsPanelShell, SettingsRow, Slider } from '@dotcraft/plugin'
import type { DesktopPluginSurfaceWrapperProps } from '@dotcraft/plugin'
import { useEffect, useState, type JSX } from 'react'
import type { ThemeAppearance } from '../../../shared/themes'
import { choiceAt, useChoices } from './choices'
import { useAppearance, useSettings } from './hooks'
import { stringsFor } from './i18n'
import { setLibraryPageOpen, useLibraryPageOpen } from './libraryPage'
import { MIN_SURFACE_OPACITY, previewSettings, setSettings } from './settings'
import { useThemeLibrary } from './themeLibrary'
import { ThemePicker } from './ThemePicker'

export function ThemeLibraryPage({ host, context, children }: DesktopPluginSurfaceWrapperProps<'settings.appearance'>): JSX.Element {
  const open = useLibraryPageOpen()
  useEffect(() => () => setLibraryPageOpen(false), [])
  if (!open) return <>{children}</>
  return <ThemeLibrary host={host} parentLabel={context.title} />
}

function ThemeLibrary({ host, parentLabel }: Pick<DesktopPluginSurfaceWrapperProps, 'host'> & { parentLabel: string }): JSX.Element | null {
  const settings = useSettings()
  const strings = stringsFor(host.environment.locale)
  const appearance = useAppearance(host)
  const [tab, setTab] = useState<ThemeAppearance>(appearance)
  useChoices()
  useThemeLibrary()
  if (!settings) return null

  const active = choiceAt({ kind: 'everywhere' }, tab) ?? { kind: 'none' as const }
  const opacity = Math.round(settings.surfaceOpacity * 100)

  return (
    <SettingsPanelShell
      title={strings.settingsTitle}
      description={strings.settingsDescription}
      breadcrumb={(
        <SettingsBreadcrumb
          parentLabel={parentLabel}
          currentLabel={strings.settingsTitle}
          onBack={() => setLibraryPageOpen(false)}
        />
      )}
    >
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
