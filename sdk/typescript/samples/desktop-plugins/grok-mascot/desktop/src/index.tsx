import type { DesktopPluginActivate } from '@dotcraft/plugin'
import {
  GROK_COLORS,
  getAppearance,
  initializeAppearance,
  setAppearance,
  type GrokColorChoice
} from './appearance'
import { AppearanceSettingsPage } from './AppearanceSettingsPage'
import { ComposerMascot } from './ComposerMascot'
import { MascotIcon } from './MascotIcon'
import { MascotStatusRing } from './MascotStatusRing'
import { stringsFor, translationsOf } from './i18n'
import { startWallpaperAwareness } from './wallpaperAwareness'
import './styles/index.css'

export const activate: DesktopPluginActivate = async (host) => {
  await initializeAppearance(host.settings)
  host.effect(() => startWallpaperAwareness(host))

  host.ui.replace('composer.mascot', ComposerMascot)
  host.ui.add('composer.mascot', MascotStatusRing)

  return {
    settingsPages: [
      {
        id: 'appearance',
        label: { default: 'Grok Mascot', translations: translationsOf('settingsLabel') },
        icon: MascotIcon,
        component: AppearanceSettingsPage
      }
    ],
    commands: [
      {
        id: 'next-color',
        label: { default: 'Grok: next color', translations: translationsOf('nextColorCommand') },
        description: {
          default: 'Cycles the mascot color without opening Settings.',
          translations: translationsOf('nextColorDescription')
        },
        icon: MascotIcon,
        execute: () => {
          const strings = stringsFor(host.environment.locale)
          const appearance = getAppearance()
          if (!appearance) return
          const order: readonly GrokColorChoice[] = ['auto', ...GROK_COLORS]
          const next = order[(order.indexOf(appearance.color) + 1) % order.length]
          setAppearance({ color: next })
          const label = next === 'auto' ? strings.automatic : strings.colors[next]
          host.ui.showToast({ message: strings.colorToast.replace('{color}', label), tone: 'success' })
        }
      }
    ]
  }
}
