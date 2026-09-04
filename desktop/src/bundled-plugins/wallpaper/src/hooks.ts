import type { DesktopPluginHost } from '@dotcraft/plugin'
import { useEffect, useState, useSyncExternalStore } from 'react'
import { imagesRevision, listImages, subscribeImages, urlForImage, type StoredImage } from './imageStore'
import { getSettings, subscribeSettings, type WallpaperSettings } from './settings'

export function useSettings(): WallpaperSettings | null {
  return useSyncExternalStore(subscribeSettings, getSettings, getSettings)
}

export function useImagesRevision(): number {
  return useSyncExternalStore(subscribeImages, imagesRevision, imagesRevision)
}

export function useResolvedTheme(host: DesktopPluginHost): 'light' | 'dark' {
  const [theme, setTheme] = useState<'light' | 'dark'>(host.environment.theme)
  useEffect(() => {
    setTheme(host.environment.theme)
    return host.environment.onChange((environment) => setTheme(environment.theme))
  }, [host])
  return theme
}

export function useStoredImages(revision: number): readonly StoredImage[] {
  const [images, setImages] = useState<readonly StoredImage[]>([])
  useEffect(() => {
    let live = true
    void listImages().then((loaded) => {
      if (live) setImages(loaded)
    })
    return () => {
      live = false
    }
  }, [revision])
  return images
}

export function useImageUrl(id: string | null, revision: number): string | null {
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => {
    if (id === null) {
      setUrl(null)
      return
    }
    let live = true
    void listImages().then((images) => {
      if (!live) return
      const match = images.find((image) => image.id === id)
      setUrl(match === undefined ? null : urlForImage(match))
    })
    return () => {
      live = false
    }
  }, [id, revision])
  return url
}
