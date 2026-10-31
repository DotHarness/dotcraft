import { app, BrowserWindow, dialog, type SaveDialogOptions, type WebContents } from 'electron'
import fs from 'fs/promises'
import path from 'path'

export interface SaveFileAsRequest {
  data: Uint8Array
  suggestedName: string
}

export async function saveFileAs(
  sender: WebContents,
  request: SaveFileAsRequest
): Promise<{ saved: boolean }> {
  if (!(request?.data instanceof Uint8Array) || request.data.byteLength === 0) {
    throw new Error('File data is missing.')
  }
  const target = await chooseSavePath(sender, request.suggestedName)
  if (!target) return { saved: false }
  await fs.writeFile(target, Buffer.from(request.data))
  return { saved: true }
}

/** `sourcePath` must already be resolved through the caller's access boundary. */
export async function saveFileCopyAs(sender: WebContents, sourcePath: string): Promise<{ saved: boolean }> {
  const target = await chooseSavePath(sender, path.basename(sourcePath))
  if (!target) return { saved: false }
  if (path.resolve(target) !== path.resolve(sourcePath)) await fs.copyFile(sourcePath, target)
  return { saved: true }
}

async function chooseSavePath(sender: WebContents, suggestedName: string): Promise<string | null> {
  const name = sanitizeFileName(suggestedName)
  const ext = path.extname(name).slice(1)
  const options: SaveDialogOptions = {
    defaultPath: path.join(app.getPath('downloads'), name),
    filters: ext ? [{ name: ext.toUpperCase(), extensions: [ext] }] : []
  }
  const owner = BrowserWindow.fromWebContents(sender)
  const result = owner && !owner.isDestroyed()
    ? await dialog.showSaveDialog(owner, options)
    : await dialog.showSaveDialog(options)
  return result.canceled || !result.filePath ? null : result.filePath
}

function sanitizeFileName(name: string): string {
  const cleaned = String(name ?? '')
    .replace(/[\\/:*?"<>|\u0000-\u001f]/g, '-')
    .trim()
  return cleaned || 'file'
}
