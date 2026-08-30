import type { DesktopPluginActivate, DesktopPluginIconProps } from '@dotcraft/plugin'
import type { JSX } from 'react'
import { listImages, releaseObjectUrls } from './imageStore'
import { stringsFor, translationsOf } from './i18n'
import { PRESETS } from './presets'
import {
  WALLPAPER_CHANGED,
  choiceFor,
  getSettings,
  initializeSettings,
  setSettings,
  subscribeSettings,
  type WallpaperChoice,
  type WallpaperSettings
} from './settings'
import { WallpaperSettingsPage } from './WallpaperSettingsPage'
import { WallpaperLayer } from './WallpaperSurfaces'
import './styles/index.css'

export const WALLPAPER_SERVICE_ID = 'wallpaper.controller'

export interface WallpaperService {
  get(): WallpaperSettings | null
  set(patch: Partial<WallpaperSettings>): WallpaperSettings | null
  subscribe(listener: (settings: WallpaperSettings) => void): () => void
}

function WallpaperIcon({ size = 16, style, ...rest }: DesktopPluginIconProps): JSX.Element {
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

async function nextChoice(current: WallpaperChoice): Promise<WallpaperChoice> {
  const stored = await listImages()
  const ring: WallpaperChoice[] = [
    { kind: 'none' },
    ...PRESETS.map((preset) => ({ kind: 'preset', id: preset.id }) as const),
    ...stored.map((image) => ({ kind: 'image', id: image.id }) as const)
  ]
  const index = ring.findIndex(
    (candidate) =>
      candidate.kind === current.kind && (candidate.kind === 'none' || candidate.id === (current as { id: string }).id)
  )
  return ring[(index + 1) % ring.length] ?? { kind: 'none' }
}

export const activate: DesktopPluginActivate = async (host) => {
  // Publish before the first await so concurrently activating consumers can resolve it.
  host.services.provide<WallpaperService>(WALLPAPER_SERVICE_ID, {
    get: getSettings,
    set: setSettings,
    subscribe: subscribeSettings
  })
  host.effect(() => releaseObjectUrls)
  host.effect(() =>
    subscribeSettings((settings) => {
      host.events.emit<WallpaperSettings>(WALLPAPER_CHANGED, settings)
    })
  )

  await initializeSettings(host.settings)
  const loaded = getSettings()
  if (loaded) host.events.emit<WallpaperSettings>(WALLPAPER_CHANGED, loaded)

  let presentationRevision = 0
  const updatePresentation = (): void => {
    const revision = ++presentationRevision
    const settings = getSettings()
    if (!settings?.enabled) {
      host.appearance.setBackdropPresentation(null)
      return
    }
    const choice = choiceFor(settings, host.environment.theme)
    if (choice.kind === 'none') {
      host.appearance.setBackdropPresentation(null)
      return
    }
    if (choice.kind === 'preset') {
      host.appearance.setBackdropPresentation({ surfaceOpacity: settings.surfaceOpacity / 100 })
      return
    }
    void listImages().then((images) => {
      if (revision !== presentationRevision) return
      host.appearance.setBackdropPresentation(
        images.some((image) => image.id === choice.id)
          ? { surfaceOpacity: settings.surfaceOpacity / 100 }
          : null
      )
    })
  }
  updatePresentation()
  host.effect(() => subscribeSettings(updatePresentation))
  host.effect(() => host.environment.onChange(updatePresentation))

  host.ui.replace('app.background', WallpaperLayer)

  return {
    settingsPages: [
      {
        id: 'wallpaper',
        label: { default: 'Wallpaper', translations: translationsOf('settingsLabel') },
        icon: WallpaperIcon,
        component: WallpaperSettingsPage
      }
    ],
    commands: [
      {
        id: 'next-scene',
        label: { default: 'Wallpaper: next scene', translations: translationsOf('nextCommand') },
        description: {
          default: 'Cycles the scene for the current theme.',
          translations: translationsOf('nextDescription')
        },
        icon: WallpaperIcon,
        execute: async () => {
          const theme = host.environment.theme
          const settings = getSettings()
          if (!settings) return
          const choice = await nextChoice(choiceFor(settings, theme))
          setSettings(theme === 'dark' ? { dark: choice } : { light: choice })
        }
      },
      {
        id: 'toggle',
        label: { default: 'Wallpaper: show or hide', translations: translationsOf('toggleCommand') },
        description: {
          default: 'Turns the wallpaper off and back on.',
          translations: translationsOf('toggleDescription')
        },
        icon: WallpaperIcon,
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
              run: () => host.navigation.openSettingsPage('wallpaper')
            }
          })
        }
      }
    ]
  }
}
