import { useSyncExternalStore } from 'react'
import { getAppearance, subscribeAppearance, type GrokAppearance } from './appearance'
import { isOverWallpaper, subscribeOverWallpaper } from './wallpaperAwareness'

export function useAppearance(): GrokAppearance | null {
  return useSyncExternalStore(subscribeAppearance, getAppearance, getAppearance)
}

export function useOverWallpaper(): boolean {
  return useSyncExternalStore(subscribeOverWallpaper, isOverWallpaper, isOverWallpaper)
}
