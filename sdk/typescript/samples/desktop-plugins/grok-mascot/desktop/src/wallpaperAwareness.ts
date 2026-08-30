import type { DesktopPluginHost } from '@dotcraft/plugin'

const WALLPAPER_SERVICE_ID = 'wallpaper.controller'
const WALLPAPER_CHANGED = 'wallpaper.settings-changed'

interface WallpaperSnapshot {
  readonly enabled: boolean
  readonly light: { readonly kind: string }
  readonly dark: { readonly kind: string }
}

interface WallpaperService {
  get(): WallpaperSnapshot | null
}

const listeners = new Set<(over: boolean) => void>()
let overWallpaper = false

export function isOverWallpaper(): boolean {
  return overWallpaper
}

export function subscribeOverWallpaper(listener: (over: boolean) => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

export function startWallpaperAwareness(host: DesktopPluginHost): () => void {
  const publish = (snapshot: WallpaperSnapshot | null | undefined): void => {
    const theme = host.environment.theme
    const next =
      snapshot != null && snapshot.enabled && snapshot[theme].kind !== 'none'
    if (next === overWallpaper) return
    overWallpaper = next
    for (const listener of listeners) listener(next)
  }

  const resolve = (): void => publish(host.services.use<WallpaperService>(WALLPAPER_SERVICE_ID)?.get())

  const stopEvent = host.events.on<WallpaperSnapshot>(WALLPAPER_CHANGED, publish)
  const stopEnvironment = host.environment.onChange(resolve)
  const settle = window.setTimeout(resolve, 0)
  resolve()

  return () => {
    window.clearTimeout(settle)
    stopEvent()
    stopEnvironment()
    listeners.clear()
    overWallpaper = false
  }
}
