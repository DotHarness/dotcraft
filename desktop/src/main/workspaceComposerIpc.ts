import { randomUUID } from 'crypto'
import { promises as fs } from 'fs'
import * as path from 'path'
import { translate, DEFAULT_LOCALE, type AppLocale } from '../shared/locales'
import { workspaceTempPath } from './workspaceTempPath'

const MAX_IMAGE_BYTES = 20 * 1024 * 1024

const MIME_TO_EXT: Record<string, string> = {
  'image/png': '.png',
  'image/jpeg': '.jpg',
  'image/jpg': '.jpg',
  'image/gif': '.gif',
  'image/webp': '.webp',
  'image/bmp': '.bmp'
}

export {
  activateFileIndexWorkspace, ensureFileIndex, warmFileSearchIndex, cleanupWorkspaceCache,
  searchWorkspaceFiles, listWorkspaceFiles, invalidateFileIndex, releaseFileIndex,
  shouldInvalidateFileIndexForWatchEvent
} from './workspaceFileIndex'
export type { FileIndexEntry, FileIndexStatus, FileIndexSource, FileMatchWire, FileListResultWire } from './workspaceFileIndexTypes'

function isPathWithin(parent: string, target: string): boolean {
  const resolvedParent = path.resolve(parent)
  const resolvedTarget = path.resolve(target)
  return resolvedTarget === resolvedParent || resolvedTarget.startsWith(`${resolvedParent}${path.sep}`)
}

function inferMimeTypeFromPath(absPath: string): string {
  const ext = path.extname(absPath).toLowerCase()
  return (
    Object.entries(MIME_TO_EXT).find(([, mappedExt]) => mappedExt === ext)?.[0]
    ?? 'image/png'
  )
}

/** Writes to `.craft/attachments/images/<uuid>.<ext>` under the workspace, not an OS temp dir. */
export async function saveImageDataUrlToTemp(
  workspaceRoot: string,
  dataUrl: string,
  suggestedFileName?: string,
  locale: AppLocale = DEFAULT_LOCALE
): Promise<string> {
  const resolved = path.resolve(workspaceRoot)
  const match = /^data:([^;]+);base64,(.+)$/s.exec(dataUrl.trim())
  if (!match) {
    throw new Error(translate(locale, 'ipc.invalidImageDataUrl'))
  }
  const mime = match[1].trim().toLowerCase()
  const b64 = match[2].replace(/\s/g, '')
  const buf = Buffer.from(b64, 'base64')
  if (buf.length > MAX_IMAGE_BYTES) {
    throw new Error(
      translate(locale, 'ipc.imageTooLarge', { bytes: buf.length, max: MAX_IMAGE_BYTES })
    )
  }
  if (!mime.startsWith('image/')) {
    throw new Error(translate(locale, 'ipc.clipboardNotImage'))
  }
  const ext = MIME_TO_EXT[mime] ?? (path.extname(suggestedFileName ?? '') || '.png')
  const dir = path.join(resolved, '.craft', 'attachments', 'images')
  await fs.mkdir(dir, { recursive: true })
  const fileName = `${randomUUID()}${ext.startsWith('.') ? ext : `.${ext}`}`
  const absPath = path.join(dir, fileName)
  await fs.writeFile(absPath, buf)
  return absPath
}

/** Also accepts the legacy `.craft/tmp/images` location, not just `.craft/attachments/images`. */
export async function readImageAsDataUrl(
  workspaceRoot: string,
  absPath: string,
  locale: AppLocale = DEFAULT_LOCALE
): Promise<string> {
  const resolvedRoot = path.resolve(workspaceRoot)
  const resolvedPath = path.resolve(absPath)
  const attachmentsDir = path.join(resolvedRoot, '.craft', 'attachments', 'images')
  const legacyTmpDir = path.join(workspaceTempPath(resolvedRoot), 'images')
  const allowed =
    isPathWithin(attachmentsDir, resolvedPath) ||
    isPathWithin(legacyTmpDir, resolvedPath)
  if (!allowed) {
    throw new Error(
      translate(locale, 'ipc.pathOutsideWorkspace', { path: absPath })
    )
  }
  const buf = await fs.readFile(resolvedPath)
  if (buf.length > MAX_IMAGE_BYTES) {
    throw new Error(
      translate(locale, 'ipc.imageTooLarge', { bytes: buf.length, max: MAX_IMAGE_BYTES })
    )
  }
  const mimeType = inferMimeTypeFromPath(resolvedPath)
  if (!mimeType.startsWith('image/')) {
    throw new Error(translate(locale, 'ipc.clipboardNotImage'))
  }
  return `data:${mimeType};base64,${buf.toString('base64')}`
}
