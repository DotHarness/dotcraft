import { createContext } from 'react'
import type { ConversationItem, ConversationTurn } from '../../../types/conversation'
import { projectInputParts } from '../../../utils/inputPresentation'
import { isVisibleUserMessage } from '../../../utils/visibleUserMessage'

export interface GalleryImage {
  key: string
  src?: string
  localPath?: string
  title?: string
  revealPath?: string
}

export const ConversationImagesContext = createContext<(() => GalleryImage[]) | null>(null)

const localImageDataUrls = new Map<string, string>()

function cachedLocalImageDataUrl(path: string): string | undefined {
  return localImageDataUrls.get(path)
}

export async function loadLocalImageDataUrl(path: string): Promise<string> {
  const cached = localImageDataUrls.get(path)
  if (cached) return cached
  const { dataUrl } = await window.api.workspace.readImageAsDataUrl({ path })
  if (!dataUrl) throw new Error('Image is unavailable.')
  localImageDataUrls.set(path, dataUrl)
  return dataUrl
}

export function resolveGalleryImageSrc(image: GalleryImage): Promise<string> {
  if (image.src) return Promise.resolve(image.src)
  if (image.localPath) return loadLocalImageDataUrl(image.localPath)
  return Promise.reject(new Error('Image is unavailable.'))
}

export function toolOutputImageKey(itemId: string, contentIndex: number): string {
  return `${itemId}-image-${contentIndex}`
}

export function userImageKeys(messageId: string, sources: string[]): string[] {
  const seen = new Map<string, number>()
  return sources.map((source) => {
    const occurrence = seen.get(source) ?? 0
    seen.set(source, occurrence + 1)
    return `${messageId}-attachment-${occurrence}-${source}`
  })
}

export function collectConversationImages(
  turns: ConversationTurn[],
  options: { localFiles: boolean }
): GalleryImage[] {
  const images: GalleryImage[] = []
  for (const turn of turns) {
    const visibleUsers = turn.items.filter(isVisibleUserMessage)
    for (const item of visibleUsers) images.push(...userMessageImages(item, options.localFiles))
    for (const item of turn.items) {
      if (isVisibleUserMessage(item)) continue
      if (item.type === 'userMessage') {
        images.push(...userMessageImages(item, options.localFiles))
        continue
      }
      if (item.type === 'imageGeneration') {
        const image = generatedImage(item, options.localFiles)
        if (image) images.push(image)
        continue
      }
      ;(item.contentItems ?? []).forEach((content, index) => {
        const data = content.dataBase64?.trim()
        if (content.type !== 'image' || !data) return
        images.push({
          key: toolOutputImageKey(item.id, index),
          src: `data:${content.mediaType?.trim() || 'image/png'};base64,${data}`
        })
      })
    }
  }
  return images
}

export function generatedImage(item: ConversationItem, localFiles: boolean): GalleryImage | null {
  const status = item.imageGenerationStatus ?? (item.status === 'completed' ? 'completed' : 'inProgress')
  const data = item.result?.trim()
  if (status !== 'completed' || !data) return null
  return {
    key: toolOutputImageKey(item.id, 0),
    src: `data:${item.mediaType?.trim() || 'image/png'};base64,${data}`,
    revealPath: generatedImageLocalPath(item, localFiles)
  }
}

function generatedImageLocalPath(item: ConversationItem, localFiles: boolean): string | undefined {
  const path = item.savedPath?.trim()
  return localFiles && path && !item.savedHostId ? path : undefined
}

function userMessageImages(item: ConversationItem, localFiles: boolean): GalleryImage[] {
  const projected = item.nativeInputParts ? projectInputParts(item.nativeInputParts) : null
  const dataUrls = projected?.imageDataUrls ?? item.imageDataUrls ?? []
  const refs = projected?.images ?? item.images ?? []
  const localRefs = localFiles ? refs : []
  const keys = userImageKeys(item.id, [...dataUrls, ...localRefs.map((ref) => ref.path)])
  return [
    ...dataUrls.map((url, index) => ({ key: keys[index], src: url })),
    ...localRefs.map((ref, index) => ({
      key: keys[dataUrls.length + index],
      src: cachedLocalImageDataUrl(ref.path),
      localPath: ref.path,
      title: ref.fileName,
      revealPath: ref.path
    }))
  ]
}

export function galleryForImage(
  image: GalleryImage,
  local: GalleryImage[],
  getConversationImages: (() => GalleryImage[]) | null
): { images: GalleryImage[]; index: number } {
  const conversation = getConversationImages?.() ?? []
  const conversationIndex = conversation.findIndex((entry) => entry.key === image.key)
  if (conversationIndex >= 0) {
    const images = [...conversation]
    images[conversationIndex] = { ...conversation[conversationIndex], ...image }
    return { images, index: conversationIndex }
  }
  const localIndex = local.findIndex((entry) => entry.key === image.key)
  return localIndex >= 0 ? { images: local, index: localIndex } : { images: [image], index: 0 }
}
