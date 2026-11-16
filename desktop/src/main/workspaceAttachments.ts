import { app } from 'electron'
import { promises as fs } from 'fs'
import * as path from 'path'
import type { AppServerConnectionSource } from '../shared/appServerBoundary'
import { MAX_ATTACHMENT_BYTES, type AttachmentReadParams } from '../shared/attachments'
import type { WindowIpcContext } from './ipcBridgeContracts'
import { mainLocale } from './ipcBridgeContracts'
import { readImageAsDataUrl, saveImageDataUrlToTemp } from './workspaceComposerIpc'
import { mimeForExtension } from './viewerMime'

function clientAttachmentRoot(): string {
  return path.join(app.getPath('temp'), 'dotcraft-client-attachments')
}

function within(root: string, candidate: string): boolean {
  const relative = path.relative(root, candidate)
  return relative === '' || (!relative.startsWith('..' + path.sep) && relative !== '..' && !path.isAbsolute(relative))
}

function requireManagedImage(root: string, candidate: string): void {
  const hostPath = /^[A-Za-z]:[\\/]|^\\\\/.test(root) ? path.win32 : path.posix
  const directory = hostPath.join(root, '.craft', 'attachments', 'images')
  const relative = hostPath.relative(directory, candidate)
  if (!relative || relative === '..' || relative.startsWith('..' + hostPath.sep) || hostPath.isAbsolute(relative)) {
    throw new Error('Image path is outside the workspace attachment folder.')
  }
  if (!['.png', '.jpg', '.jpeg', '.gif', '.webp', '.bmp'].includes(hostPath.extname(candidate).toLowerCase())) {
    throw new Error('Attachment is not a supported image.')
  }
}

export class WorkspaceAttachmentAccess {
  constructor(private readonly context: WindowIpcContext) {}

  private connection(source?: AppServerConnectionSource) {
    const { callbacks, getWireClient } = this.context
    const client = source ? callbacks.getConnectionClient?.(source.connectionKey) : getWireClient()
    if (!client || (source && client.connectionId !== source.connectionId)) {
      throw new Error('The AppServer connection is no longer active.')
    }
    const owner = callbacks.getConnectionContext(client)
    if (!owner) throw new Error('The AppServer connection is no longer active.')
    const id = client.connectionId
    return { client, workspacePath: owner.workspacePath, ensureCurrent: () => {
      if (client.connectionId !== id || !callbacks.getConnectionContext(client)) {
        throw new Error('The AppServer connection is no longer active.')
      }
    } }
  }

  async statLocalFile(params: { path: string }): Promise<{ sizeBytes: number }> {
    const info = await fs.stat(params.path)
    if (!info.isFile()) throw new Error('Attachment is not a file.')
    return { sizeBytes: info.size }
  }

  async readLocalFile(params: { path: string }): Promise<{ dataBase64: string }> {
    const { sizeBytes } = await this.statLocalFile(params)
    if (sizeBytes > MAX_ATTACHMENT_BYTES) throw new Error('Attachment exceeds the file limit.')
    const bytes = await fs.readFile(params.path)
    if (bytes.length > MAX_ATTACHMENT_BYTES) throw new Error('Attachment exceeds the file limit.')
    return { dataBase64: bytes.toString('base64') }
  }

  async saveImage(params: { dataUrl: string; fileName?: string; source?: AppServerConnectionSource }): Promise<{ path: string }> {
    const owner = this.connection(params.source)
    const root = owner.client.isRemoteHost ? clientAttachmentRoot() : owner.workspacePath
    const absolutePath = await saveImageDataUrlToTemp(root, params.dataUrl, params.fileName, mainLocale(this.context.callbacks))
    owner.ensureCurrent()
    return { path: absolutePath }
  }

  async readImage(params: AttachmentReadParams): Promise<{ dataUrl: string }> {
    const owner = this.connection(params.source)
    if (within(clientAttachmentRoot(), path.resolve(params.path))) {
      return { dataUrl: await readImageAsDataUrl(clientAttachmentRoot(), params.path) }
    }
    if (!owner.client.isRemoteHost) {
      const dataUrl = await readImageAsDataUrl(owner.workspacePath, params.path, mainLocale(this.context.callbacks))
      owner.ensureCurrent()
      return { dataUrl }
    }
    requireManagedImage(owner.workspacePath, params.path)
    const { dataBase64 } = await owner.client.sendRequest<{ dataBase64: string }>('fs/readFile', { path: params.path })
    owner.ensureCurrent()
    if (Buffer.byteLength(dataBase64, 'base64') > MAX_ATTACHMENT_BYTES) throw new Error('Image exceeds the file limit.')
    const leaf = params.path.replace(/\\/g, '/').split('/').pop() ?? ''
    return { dataUrl: `data:${mimeForExtension(path.extname(leaf))};base64,${dataBase64}` }
  }
}
