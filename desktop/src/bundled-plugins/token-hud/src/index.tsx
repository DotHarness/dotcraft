import type { DesktopPluginActivate, DesktopPluginIconProps } from '@dotcraft/plugin'
import type { JSX } from 'react'
import { TokenHud } from './TokenHud'
import { TokenHudSettingsPage } from './TokenHudSettingsPage'
import { stringsFor, translationsOf } from './i18n'
import { getSettings, initializeSettings, setSettings } from './settings'
import { startUsageFeed } from './usage'
import './styles/index.css'

function TokenHudIcon({ size = 16, style, ...rest }: DesktopPluginIconProps): JSX.Element {
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
      <path d="M3.34 19a10 10 0 1 1 17.32 0" />
      <path d="m12 14 4-4" />
    </svg>
  )
}

export const activate: DesktopPluginActivate = async (host) => {
  await initializeSettings(host.settings)
  host.effect(() => startUsageFeed(host))

  host.ui.add('composer.status.trailing', TokenHud)

  return {
    settingsPages: [
      {
        id: 'token-hud',
        label: { default: 'Token HUD', translations: translationsOf('settingsLabel') },
        icon: TokenHudIcon,
        component: TokenHudSettingsPage
      }
    ],
    commands: [
      {
        id: 'toggle',
        label: { default: 'Token HUD: show or hide', translations: translationsOf('toggleCommand') },
        description: {
          default: 'Shows or hides the performance readout.',
          translations: translationsOf('toggleDescription')
        },
        icon: TokenHudIcon,
        execute: () => {
          const strings = stringsFor(host.environment.locale)
          const settings = getSettings()
          if (!settings) return
          const next = setSettings({ visible: !settings.visible })
          if (!next) return
          host.ui.showToast({
            message: next.visible ? strings.shownToast : strings.hiddenToast,
            tone: 'neutral'
          })
        }
      }
    ]
  }
}
