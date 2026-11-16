import { createContext } from 'react'
import type { AppServerConnectionSource } from '../../../../shared/appServerBoundary'
import { useConnectionStore } from '../../../stores/connectionStore'
import { useThreadStore } from '../../../stores/threadStore'
import { getThreadConnection } from '../../../stores/threadConnectionStore'
import type { ConversationItem, ConversationTurn } from '../../../types/conversation'
import { projectInputParts } from '../../../utils/inputPresentation'
import { isVisibleUserMessage } from '../../../utils/visibleUserMessage'

export interface ImageReadScope {
  source?: AppServerConnectionSource
  workspacePath?: string
}

export const ImageReadScopeContext = createContext<ImageReadScope | null>(null)

export function attachmentImageScope(threadId?: string, workspacePath?: string): ImageReadScope {
  const target = threadId ? getThreadConnection(threadId) : undefined
  const foreground = useConnectionStore.getState()
  const source = target ? { connectionKey: target.connectionKey, connectionId: target.connectionId } : (foreground.connectionKey && foreground.connectionId
    ? { connectionKey: foreground.connectionKey, connectionId: foreground.connectionId } : undefined)
  const threads = useThreadStore.getState()
  const thread = threads.activeThread?.id === threadId ? threads.activeThread : threads.threadList.find(t => t.id === threadId)
  return { source, workspacePath: thread?.workspacePath ?? workspacePath }
}

export interface GalleryImage {
  key: string
  src?: string
  localPath?: string
  title?: string
  revealPath?: string
  scope?: ImageReadScope
}

export const ConversationImagesContext = createContext<(() => GalleryImage[]) | null>(null)

const localImageDataUrls = new Map<string, string>()

function imageCacheKey(path: string, scope: ImageReadScope): string {
  return JSON.stringify([scope.source?.connectionKey, scope.source?.connectionId, scope.workspacePath, path])
}

function cachedLocalImageDataUrl(path: string, scope: ImageReadScope): string | undefined {
  return localImageDataUrls.get(imageCacheKey(path, scope))
}

export async function loadScopedImageDataUrl(path: string, scope: ImageReadScope): Promise<string> {
  if (scope.workspacePath) {
    const windows = /^[A-Za-z]:[\\/]|^\\\\/.test(scope.workspacePath)
    const normalize = (value: string) => {
      const normalized = value.replace(/\\/g, '/').replace(/\/+$/, '')
      return windows ? normalized.toLowerCase() : normalized
    }
    if (!normalize(path).startsWith(normalize(scope.workspacePath) + '/')) throw new Error('Image belongs to a different workspace.')
  }
  const key = imageCacheKey(path, scope)
  const cached = localImageDataUrls.get(key)
  if (cached) return cached
  const { dataUrl } = await window.api.workspace.readImageAsDataUrl({ path, ...scope })
  if (!dataUrl) throw new Error('Image is unavailable.')
  localImageDataUrls.set(key, dataUrl)
  return dataUrl
}

export function loadLocalImageDataUrl(path: string, threadId?: string, workspacePath?: string): Promise<string> {
  return loadScopedImageDataUrl(path, attachmentImageScope(threadId, workspacePath))
}

export function resolveGalleryImageSrc(image: GalleryImage): Promise<string> {
  if (image.src) return Promise.resolve(image.src)
  if (image.localPath) return image.scope ? loadScopedImageDataUrl(image.localPath, image.scope) : loadLocalImageDataUrl(image.localPath)
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
  options: { localFiles: boolean; scope?: ImageReadScope }
): GalleryImage[] {
  const images: GalleryImage[] = []
  for (const turn of turns) {
    const visibleUsers = turn.items.filter(isVisibleUserMessage)
    for (const item of visibleUsers) images.push(...userMessageImages(item, options.localFiles, options.scope))
    for (const item of turn.items) {
      if (isVisibleUserMessage(item)) continue
      if (item.type === 'userMessage') {
        images.push(...userMessageImages(item, options.localFiles, options.scope))
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

function userMessageImages(item: ConversationItem, localFiles: boolean, scope?: ImageReadScope): GalleryImage[] {
  const projected = item.nativeInputParts ? projectInputParts(item.nativeInputParts) : null
  const dataUrls = projected?.imageDataUrls ?? item.imageDataUrls ?? []
  const refs = projected?.images ?? item.images ?? []
  const localRefs = localFiles || scope?.source ? refs : []
  const keys = userImageKeys(item.id, [...dataUrls, ...localRefs.map((ref) => ref.path)])
  return [
    ...dataUrls.map((url, index) => ({ key: keys[index], src: url })),
    ...localRefs.map((ref, index) => ({
      key: keys[dataUrls.length + index],
      src: cachedLocalImageDataUrl(ref.path, scope ?? attachmentImageScope()),
      localPath: ref.path,
      title: ref.fileName,
      revealPath: localFiles ? ref.path : undefined,
      scope: scope ?? attachmentImageScope()
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
