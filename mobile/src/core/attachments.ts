import type { FileAttachment, MessageDraft, PhotoAttachment } from './draft'

export const MAX_FILE_BYTES = 2 * 1024 * 1024
export const MAX_PHOTO_SIDE = 2048
export const MAX_MESSAGE_PHOTO_CHARS = 3 * 1024 * 1024

// Photos travel inside the turn request, which must fit the AppServer's 4 MB message limit.
export function withinPhotoBudget(current: PhotoAttachment[], picked: PhotoAttachment[]): { accepted: PhotoAttachment[]; refused: number } {
  const accepted: PhotoAttachment[] = []
  let used = current.reduce((total, photo) => total + photo.dataUrl.length, 0)
  for (const photo of picked) {
    if (used + photo.dataUrl.length > MAX_MESSAGE_PHOTO_CHARS) continue
    used += photo.dataUrl.length
    accepted.push(photo)
  }
  return { accepted, refused: picked.length - accepted.length }
}

export interface AttachmentFileSystem {
  createDirectory(path: string): Promise<void>
  writeFile(path: string, dataBase64: string): Promise<void>
}

export class AttachmentUploadError extends Error {
  constructor(
    readonly file: string,
    cause: unknown,
  ) {
    super(`Couldn’t upload ${file}.`, { cause })
    this.name = 'AttachmentUploadError'
  }
}

export type SendFailure = { kind: 'upload'; file: string } | { kind: 'send' }

export function fitWithin(width: number, height: number, max = MAX_PHOTO_SIDE): { width: number } | { height: number } | null {
  if (Math.max(width, height) <= max) return null
  return width >= height ? { width: max } : { height: max }
}

export function base64Bytes(data: string): number {
  const padding = data.endsWith('==') ? 2 : data.endsWith('=') ? 1 : 0
  return Math.floor((data.length * 3) / 4) - padding
}

function safeName(name: string): string {
  const cleaned = [...name]
    .filter((char) => char.charCodeAt(0) >= 32)
    .join('')
    .replace(/[\\/:*?"<>|]/g, '_')
    .trim()
  return cleaned && cleaned !== '.' && cleaned !== '..' ? cleaned : 'file'
}

function join(root: string, ...names: string[]): string {
  const separator = root.includes('\\') && !root.includes('/') ? '\\' : '/'
  return [root.replace(/[\\/]+$/, ''), ...names].join(separator)
}

export async function uploadAttachments(
  fs: AttachmentFileSystem,
  projectPath: string,
  files: FileAttachment[],
  newId: () => string,
): Promise<{ path: string; name: string }[]> {
  const uploaded: { path: string; name: string }[] = []
  for (const file of files) {
    const name = safeName(file.name)
    const directory = join(projectPath, '.craft', 'attachments', newId())
    const path = join(directory, name)
    try {
      await fs.createDirectory(directory)
      await fs.writeFile(path, file.dataBase64)
    } catch (error) {
      throw new AttachmentUploadError(file.name, error)
    }
    uploaded.push({ path, name })
  }
  return uploaded
}

export async function sendDraft(draft: MessageDraft, send: (draft: MessageDraft) => Promise<void>): Promise<SendFailure | null> {
  try {
    await send(draft)
    return null
  } catch (error) {
    return error instanceof AttachmentUploadError ? { kind: 'upload', file: error.file } : { kind: 'send' }
  }
}
