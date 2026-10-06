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
  const name = sanitizeFileName(request.suggestedName)
  const ext = path.extname(name).slice(1)
  const options: SaveDialogOptions = {
    defaultPath: path.join(app.getPath('downloads'), name),
    filters: ext ? [{ name: ext.toUpperCase(), extensions: [ext] }] : []
  }
  const owner = BrowserWindow.fromWebContents(sender)
  const result = owner && !owner.isDestroyed()
    ? await dialog.showSaveDialog(owner, options)
    : await dialog.showSaveDialog(options)
  if (result.canceled || !result.filePath) return { saved: false }
  await fs.writeFile(result.filePath, Buffer.from(request.data))
  return { saved: true }
}

function sanitizeFileName(name: string): string {
  const cleaned = String(name ?? '')
    .replace(/[\\/:*?"<>|\u0000-\u001f]/g, '-')
    .trim()
  return cleaned || 'file'
}
