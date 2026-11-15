import type { DesktopPluginActivate, DesktopPluginIconProps } from '@dotcraft/plugin'
import type { JSX } from 'react'
import { initializeChoices, subscribeChoices } from './choices'
import { closeComposerPicker, isComposerPickerOpen, openComposerPicker } from './composerPicker'
import { ComposerThemePicker } from './ComposerThemePicker'
import { stringsFor, translationsOf } from './i18n'
import { getSettings, initializeSettings, setSettings, subscribeSettings } from './settings'
import { activeGlass, activeTheme, initializeThemeLibrary, isBackdropReady, subscribeThemeLibrary } from './themeLibrary'
import { ThemeSettingsPage } from './ThemeSettingsPage'
import { ThemeLayer } from './ThemeSurfaces'
import './styles/index.css'

function ThemeIcon({ size = 16, style, ...rest }: DesktopPluginIconProps): JSX.Element {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      style={style}
      {...rest}
    >
      <rect x="3" y="4" width="18" height="16" rx="3" />
      <circle cx="8.5" cy="9.5" r="1.6" />
      <path d="m4 17 5-5 3.5 3.5L16 12l4 4" />
    </svg>
  )
}

export const activate: DesktopPluginActivate = async (host) => {
  await initializeSettings(host.settings)
  host.effect(() => initializeChoices(host))
  host.effect(() => initializeThemeLibrary(host))

  const updatePresentation = (): void => {
    const appearance = host.environment.theme
    const theme = activeTheme(appearance)
    const ready = theme !== null && isBackdropReady(theme)
    const glass = ready ? activeGlass(appearance) : null
    host.appearance.setBackdropPresentation(glass)
    host.appearance.setThemeSeedOverride(ready && theme.colors ? { [appearance]: theme.colors } : null)
  }
  updatePresentation()
  host.effect(() => subscribeSettings(updatePresentation))
  host.effect(() => subscribeChoices(updatePresentation))
  host.effect(() => host.environment.onChange(updatePresentation))
  host.effect(() => subscribeThemeLibrary(updatePresentation))
  host.effect(() => () => {
    host.appearance.setThemeSeedOverride(null)
    host.appearance.setBackdropPresentation(null)
  })

  host.ui.replace('app.background', ThemeLayer)
  host.ui.add('composer.before', ComposerThemePicker)
  host.effect(() => closeComposerPicker)

  return {
    settingsPages: [
      {
        id: 'themes',
        label: { default: 'Themes', translations: translationsOf('settingsLabel') },
        icon: ThemeIcon,
        component: ThemeSettingsPage
      }
    ],
    commands: [
      {
        id: 'pick',
        label: { default: 'Theme', translations: translationsOf('pickCommand') },
        description: {
          default: 'Choose a theme for this chat, this project, or everywhere.',
          translations: translationsOf('pickDescription')
        },
        icon: ThemeIcon,
        isAvailable: () => getSettings()?.enabled === true,
        execute: () => {
          if (isComposerPickerOpen()) closeComposerPicker()
          else openComposerPicker()
        }
      },
      {
        id: 'toggle',
        label: { default: 'Toggle themes', translations: translationsOf('toggleCommand') },
        description: {
          default: 'Turns themes off and back on.',
          translations: translationsOf('toggleDescription')
        },
        icon: ThemeIcon,
        execute: () => {
          const strings = stringsFor(host.environment.locale)
          const settings = getSettings()
          if (!settings) return
          const next = setSettings({ enabled: !settings.enabled })
          if (!next) return
          host.ui.showToast({
            message: next.enabled ? strings.toggledOn : strings.toggledOff,
            tone: 'neutral',
            action: {
              label: strings.openSettings,
              run: () => host.navigation.openSettingsPage('themes')
            }
          })
        }
      }
    ]
  }
}
