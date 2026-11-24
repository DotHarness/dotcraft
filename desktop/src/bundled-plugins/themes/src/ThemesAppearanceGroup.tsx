import { Button, PillSwitch, SettingsGroup, SettingsRow } from '@dotcraft/plugin'
import type { DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import type { JSX } from 'react'
import { choiceAt, useChoices } from './choices'
import { useAppearance, useSettings } from './hooks'
import { stringsFor, themeDisplayName } from './i18n'
import { setLibraryPageOpen } from './libraryPage'
import { setSettings } from './settings'
import { cancelThemePreview, useThemeLibrary } from './themeLibrary'
import { ThemeThumbnail } from './ThemePicker'

export function ThemesAppearanceGroup({ host }: DesktopPluginSurfaceProps<'settings.appearance.theme'>): JSX.Element | null {
  const settings = useSettings()
  const strings = stringsFor(host.environment.locale)
  const appearance = useAppearance(host)
  const library = useThemeLibrary()
  useChoices()
  if (!settings) return null

  const choice = choiceAt({ kind: 'everywhere' }, appearance)
  const theme = choice?.kind === 'theme' ? library.themes.find((entry) => entry.id === choice.id) : undefined

  return (
    <SettingsGroup title={strings.settingsTitle}>
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
      <SettingsRow>
        <div className="dcw-appearance-theme" data-disabled={settings.enabled ? undefined : 'true'}>
          <span className="dcw-appearance-theme-thumb" aria-hidden="true">
            {theme && <ThemeThumbnail theme={theme} />}
          </span>
          <span className="dcw-appearance-theme-name">
            {theme ? themeDisplayName(theme, host.environment.locale) : strings.noneTile}
          </span>
          <Button variant="secondary" size="sm" onClick={() => setLibraryPageOpen(true)}>
            {strings.manage}
          </Button>
        </div>
      </SettingsRow>
    </SettingsGroup>
  )
}
