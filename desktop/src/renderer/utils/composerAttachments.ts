import type { ComposerFileAttachment } from '../types/conversation'

const IMAGE_EXTENSIONS = new Set(['.png', '.jpg', '.jpeg', '.gif', '.webp', '.bmp'])

interface DataTransferItemWithEntry extends DataTransferItem {
  webkitGetAsEntry?: () => { isDirectory?: boolean } | null
}

export type DroppedFilePathResolver = (file: File) => string

export interface ClassifiedDroppedComposerFiles {
  imageFiles: File[]
  fileAttachments: ComposerFileAttachment[]
  skippedCount: number
}

export function extForFile(name: string): string {
  const i = name.lastIndexOf('.')
  return i >= 0 ? name.slice(i).toLowerCase() : ''
}

export function isImageFile(file: File): boolean {
  if (file.type.startsWith('image/')) return true
  return IMAGE_EXTENSIONS.has(extForFile(file.name))
}

export function normalizeComposerFileAttachment(
  path: string,
  fileName?: string
): ComposerFileAttachment | null {
  const normalizedPath = path.trim()
  if (normalizedPath.length === 0) return null
  const normalizedName =
    fileName?.trim() || normalizedPath.split(/[/\\]/).pop() || normalizedPath
  return {
    path: normalizedPath,
    fileName: normalizedName
  }
}

export function mergeComposerFileAttachments(
  existing: ComposerFileAttachment[],
  incoming: Array<ComposerFileAttachment | { path: string; fileName?: string }>
): ComposerFileAttachment[] {
  const seen = new Set<string>()
  const merged: ComposerFileAttachment[] = []

  for (const file of existing) {
    const normalized = normalizeComposerFileAttachment(file.path, file.fileName)
    if (!normalized || seen.has(normalized.path)) continue
    seen.add(normalized.path)
    merged.push(normalized)
  }

  for (const file of incoming) {
    const normalized = normalizeComposerFileAttachment(file.path, file.fileName)
    if (!normalized || seen.has(normalized.path)) continue
    seen.add(normalized.path)
    merged.push(normalized)
  }

  return merged
}

function classifyDroppedFile(
  file: File,
  seenFilePaths: Set<string>,
  resolvePath: DroppedFilePathResolver
): { kind: 'image'; file: File } | { kind: 'file'; file: ComposerFileAttachment } | { kind: 'skip' } {
  if (isImageFile(file)) {
    return { kind: 'image', file }
  }

  const normalized = normalizeComposerFileAttachment(resolvePath(file), file.name)
  if (!normalized) return { kind: 'skip' }
  if (seenFilePaths.has(normalized.path)) return { kind: 'skip' }

  seenFilePaths.add(normalized.path)
  return { kind: 'file', file: normalized }
}

export function classifyDroppedComposerFiles(
  dataTransfer: Pick<DataTransfer, 'files' | 'items'>,
  resolvePath: DroppedFilePathResolver = () => ''
): ClassifiedDroppedComposerFiles {
  const imageFiles: File[] = []
  const fileAttachments: ComposerFileAttachment[] = []
  let skippedCount = 0
  const seenFilePaths = new Set<string>()

  const items = Array.from(dataTransfer.items ?? [])
  if (items.length > 0) {
    for (const item of items) {
      if (item.kind !== 'file') continue
      const entry = (item as DataTransferItemWithEntry).webkitGetAsEntry?.()
      if (entry?.isDirectory) {
        skippedCount += 1
        continue
      }

      const file = item.getAsFile()
      if (!file) {
        skippedCount += 1
        continue
      }

      const classified = classifyDroppedFile(file, seenFilePaths, resolvePath)
      if (classified.kind === 'image') {
        imageFiles.push(classified.file)
      } else if (classified.kind === 'file') {
        fileAttachments.push(classified.file)
      } else {
        skippedCount += 1
      }
    }

    return { imageFiles, fileAttachments, skippedCount }
  }

  for (const file of Array.from(dataTransfer.files ?? [])) {
    const classified = classifyDroppedFile(file, seenFilePaths, resolvePath)
    if (classified.kind === 'image') {
      imageFiles.push(classified.file)
    } else if (classified.kind === 'file') {
      fileAttachments.push(classified.file)
    } else {
      skippedCount += 1
    }
  }

  return { imageFiles, fileAttachments, skippedCount }
}
