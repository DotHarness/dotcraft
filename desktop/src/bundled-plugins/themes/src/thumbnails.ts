import { useCallback, useSyncExternalStore } from 'react'
import type { Theme } from '../../../shared/themes'
import { posterUrl } from './sceneDocument'

const MAX_TILE_WIDTH = 280
const MAX_TILE_HEIGHT = 80

interface Thumbnail {
  readonly revision: number
  readonly ready: Promise<void>
  url: string | null
}

const thumbnails = new Map<string, Thumbnail>()

async function rasterize(source: string, vector: boolean): Promise<string> {
  const image = new Image()
  image.src = source
  await image.decode()
  const width = image.naturalWidth || 320
  const height = image.naturalHeight || 180
  const density = Math.min(window.devicePixelRatio, 2)
  const cover = Math.max((MAX_TILE_WIDTH * density) / width, (MAX_TILE_HEIGHT * density) / height)
  const scale = vector ? cover : Math.min(1, cover)
  const canvas = document.createElement('canvas')
  canvas.width = Math.max(1, Math.round(width * scale))
  canvas.height = Math.max(1, Math.round(height * scale))
  canvas.getContext('2d')!.drawImage(image, 0, 0, canvas.width, canvas.height)
  return canvas.toDataURL('image/webp', 0.9)
}

function thumbnailOf(theme: Theme): Thumbnail | null {
  const { backdrop } = theme
  if (backdrop?.kind !== 'scene' && backdrop?.kind !== 'image') return null
  const cached = thumbnails.get(theme.id)
  if (cached?.revision === theme.revision) return cached
  const vector = backdrop.kind === 'scene'
  const source = vector ? posterUrl(backdrop.poster) : backdrop.image
  const thumbnail: Thumbnail = {
    revision: theme.revision,
    url: null,
    ready: rasterize(source, vector)
      .catch(() => source)
      .then((url) => {
        thumbnail.url = url
      })
  }
  thumbnails.set(theme.id, thumbnail)
  return thumbnail
}

export function useThemeThumbnail(theme: Theme): string | null {
  const thumbnail = thumbnailOf(theme)
  const subscribe = useCallback((notify: () => void) => {
    let live = true
    void thumbnail?.ready.then(() => {
      if (live) notify()
    })
    return () => {
      live = false
    }
  }, [thumbnail])
  return useSyncExternalStore(subscribe, () => thumbnail?.url ?? null)
}
