import type { ContextMenuEntry } from '../../ui/ContextMenu'
import { addToast } from '../../../stores/toastStore'
import { useUIStore } from '../../../stores/uiStore'
import { resolveGalleryImageSrc, type GalleryImage } from './galleryImages'

type Translate = (key: string, vars?: Record<string, string | number>) => string

async function imageSourceToBlob(src: string): Promise<Blob> {
  const match = /^data:([^;,]+)?(;base64)?,(.*)$/s.exec(src)
  if (match) {
    const mediaType = match[1] || 'application/octet-stream'
    if (!match[2]) return new Blob([decodeURIComponent(match[3])], { type: mediaType })
    const binary = atob(match[3])
    const bytes = new Uint8Array(binary.length)
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i)
    return new Blob([bytes], { type: mediaType })
  }
  const response = await fetch(src)
  if (!response.ok) throw new Error(`HTTP ${response.status}`)
  return response.blob()
}

async function toPngBlob(blob: Blob): Promise<Blob> {
  if (blob.type === 'image/png') return blob
  const bitmap = await createImageBitmap(blob)
  try {
    const canvas = document.createElement('canvas')
    canvas.width = bitmap.width
    canvas.height = bitmap.height
    const context = canvas.getContext('2d')
    if (!context) throw new Error('Canvas is unavailable.')
    context.drawImage(bitmap, 0, 0)
    const png = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/png'))
    if (!png) throw new Error('Could not encode image.')
    return png
  } finally {
    bitmap.close()
  }
}

function blobToDataUrl(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as string)
    reader.onerror = () => reject(reader.error ?? new Error('Could not read image.'))
    reader.readAsDataURL(blob)
  })
}

function imageExtension(mediaType: string): string {
  const subtype = mediaType.split('/')[1]?.toLowerCase() ?? ''
  if (subtype === 'jpeg') return 'jpg'
  if (subtype === 'svg+xml') return 'svg'
  return /^[a-z0-9]+$/.test(subtype) ? subtype : 'png'
}

function canCopyImages(): boolean {
  return typeof ClipboardItem !== 'undefined' && typeof navigator.clipboard?.write === 'function'
}

async function copyImage(image: GalleryImage, t: Translate): Promise<void> {
  try {
    const png = await toPngBlob(await imageSourceToBlob(await resolveGalleryImageSrc(image)))
    await navigator.clipboard.write([new ClipboardItem({ 'image/png': png })])
    addToast(t('toast.copied'), 'success', 2000)
  } catch {
    addToast(t('conversation.image.copyFailed'), 'error')
  }
}

export async function downloadImage(image: GalleryImage, t: Translate, locale: string): Promise<void> {
  try {
    const blob = await imageSourceToBlob(await resolveGalleryImageSrc(image))
    const formattedDate = new Intl.DateTimeFormat(locale, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    }).format(new Date())
    const suggestedName = `${t('conversation.image.downloadFileName', { formattedDate })}.${imageExtension(blob.type)}`
    await window.api.shell.saveFileAs({ data: new Uint8Array(await blob.arrayBuffer()), suggestedName })
  } catch {
    addToast(t('conversation.image.downloadFailed'), 'error')
  }
}

async function addImageToChat(image: GalleryImage, t: Translate): Promise<void> {
  try {
    const blob = await imageSourceToBlob(await resolveGalleryImageSrc(image))
    const mimeType = blob.type || 'image/png'
    const dataUrl = await blobToDataUrl(blob)
    const fileName = image.title?.trim() || `image.${imageExtension(mimeType)}`
    useUIStore.getState().requestComposerImageAttachment({ dataUrl, fileName, mimeType })
  } catch {
    addToast(t('conversation.reference.openFailed'), 'warning')
  }
}

async function revealImage(path: string, t: Translate): Promise<void> {
  try {
    await window.api.shell.revealLocalPath(path)
  } catch {
    addToast(t('conversation.reference.openFailed'), 'warning')
  }
}

function revealImageLabel(t: Translate): string {
  const platform = window.api.platform
  if (platform === 'darwin') return t('conversation.image.revealInFinder')
  if (platform === 'win32') return t('conversation.image.openInExplorer')
  return t('conversation.image.openInFileManager')
}

export function imageContextMenuEntries(
  image: GalleryImage,
  { t, locale, onOpen }: { t: Translate; locale: string; onOpen?: () => void }
): ContextMenuEntry[] {
  const entries: ContextMenuEntry[] = []
  if (onOpen) entries.push({ label: t('conversation.image.open'), onClick: onOpen })
  entries.push({ label: t('conversation.image.addToChat'), onClick: () => { void addImageToChat(image, t) } })
  if (canCopyImages()) {
    entries.push({ label: t('conversation.copyImage'), onClick: () => { void copyImage(image, t) } })
  }
  const revealPath = image.revealPath
  if (revealPath) {
    entries.push({ label: revealImageLabel(t), onClick: () => { void revealImage(revealPath, t) } })
  }
  entries.push({ label: t('conversation.image.downloadCopy'), onClick: () => { void downloadImage(image, t, locale) } })
  return entries
}
