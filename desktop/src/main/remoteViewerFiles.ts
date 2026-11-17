import * as path from 'path'
import type { WebContents } from 'electron'
import type { DesktopAppServerClient } from './DesktopAppServerClient'
import type {
  ClassifyResult,
  DirEntryWire,
  ListDirResult,
  ReadTextResult,
  TextFileChangedPayload,
  ViewerContentClass,
  WriteTextParams,
  WriteTextResult
} from '../shared/viewer/types'
import { mimeForExtension } from './viewerMime'
import { contentClassForExtension, decodeTextFile, encodeTextFile, sniffContentClass } from './viewerIpc'

const MAX_REMOTE_FILE_BYTES = 10 * 1024 * 1024
const REMOTE_PREVIEW_CLASSES = new Set<ViewerContentClass>(['text', 'image', 'pdf'])
const EXPLORER_SKIP_NAMES = new Set(['.git'])

type RemoteFileClient = Pick<DesktopAppServerClient, 'sendRequest' | 'onNotification' | 'on'>

interface RemoteMetadata {
  isFile: boolean
  modifiedAtMs: number
}

interface RemoteWatch {
  sender: WebContents
  absolutePath: string
  onDestroyed: () => void
}

const watchOwners = new Map<string, RemoteViewerFiles>()
let nextWatchId = 0

export function unwatchRemoteViewerFile(subscriptionId: string, senderId: number): boolean {
  const owner = watchOwners.get(subscriptionId)
  owner?.unwatch(subscriptionId, senderId)
  return !!owner
}

function hostPath(absolutePath: string): path.PlatformPath {
  return /^[A-Za-z]:[\\/]|^\\\\/.test(absolutePath) ? path.win32 : path.posix
}

export class RemoteViewerFiles {
  private readonly watches = new Map<string, RemoteWatch>()
  private readonly writes = new Map<string, Promise<unknown>>()
  private listening = false

  constructor(private readonly client: RemoteFileClient) {}

  async requireFile(absolutePath: string): Promise<string> {
    if (!(await this.metadata(absolutePath)).isFile) throw new Error(`Not a file: ${absolutePath}`)
    return absolutePath
  }

  async readBytes(absolutePath: string): Promise<Buffer> {
    const { dataBase64 } = await this.client.sendRequest<{ dataBase64: string }>('fs/readFile', { path: absolutePath })
    return Buffer.from(dataBase64, 'base64')
  }

  async classify(absolutePath: string): Promise<ClassifyResult> {
    const metadata = await this.metadata(absolutePath)
    const bytes = await this.readBytes(absolutePath)
    const ext = hostPath(absolutePath).extname(absolutePath).toLowerCase()
    const byExtension = contentClassForExtension(ext)
    const detected = byExtension ?? sniffContentClass(bytes.subarray(0, 16))
    return {
      contentClass: REMOTE_PREVIEW_CLASSES.has(detected) ? detected : 'unsupported',
      mime: byExtension || detected !== 'text' ? mimeForExtension(ext) : 'text/plain',
      sizeBytes: bytes.length,
      mtimeMs: metadata.modifiedAtMs
    }
  }

  async readText(absolutePath: string, limitBytes = MAX_REMOTE_FILE_BYTES): Promise<ReadTextResult> {
    const metadata = await this.metadata(absolutePath)
    const bytes = await this.readBytes(absolutePath)
    return decodeTextFile(bytes.subarray(0, limitBytes), bytes.length, metadata.modifiedAtMs, limitBytes)
  }

  async writeText(params: WriteTextParams): Promise<WriteTextResult> {
    const { absolutePath } = params
    const previous = this.writes.get(absolutePath) ?? Promise.resolve()
    const task = previous.catch(() => {}).then(async (): Promise<WriteTextResult> => {
      if ((await this.metadata(absolutePath)).modifiedAtMs !== params.expectedMtimeMs) {
        return { outcome: 'conflict', current: await this.readText(absolutePath) }
      }
      const content = encodeTextFile(params)
      await this.client.sendRequest('fs/writeFile', { path: absolutePath, dataBase64: content.toString('base64') })
      const updated = await this.metadata(absolutePath)
      return { outcome: 'saved', mtimeMs: updated.modifiedAtMs, sizeBytes: content.byteLength }
    })
    this.writes.set(absolutePath, task)
    try { return await task } finally { if (this.writes.get(absolutePath) === task) this.writes.delete(absolutePath) }
  }

  async listDir(dirPath: string, rootPath?: string): Promise<ListDirResult> {
    const hostDir = hostPath(dirPath)
    const { entries } = await this.client.sendRequest<{ entries: Array<{ fileName: string; isDirectory: boolean }> }>(
      'fs/readDirectory', { path: dirPath })
    const result: DirEntryWire[] = entries
      .filter((entry) => !EXPLORER_SKIP_NAMES.has(entry.fileName))
      .map((entry) => {
        const absolutePath = hostDir.join(dirPath, entry.fileName)
        const relative = rootPath ? hostDir.relative(rootPath, absolutePath) : absolutePath
        return { name: entry.fileName, relativePath: relative.replace(/\\/g, '/'), absolutePath, isDir: entry.isDirectory }
      })
      .sort((a, b) => a.isDir !== b.isDir ? (a.isDir ? -1 : 1) : a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }))
    return { dirPath: hostDir.normalize(dirPath), entries: result }
  }

  async watch(sender: WebContents, absolutePath: string): Promise<string> {
    this.listen()
    const subscriptionId = `viewer-remote-${sender.id}-${++nextWatchId}`
    const onDestroyed = () => this.unwatch(subscriptionId, sender.id)
    this.watches.set(subscriptionId, { sender, absolutePath, onDestroyed })
    watchOwners.set(subscriptionId, this)
    sender.once('destroyed', onDestroyed)
    try {
      await this.client.sendRequest('fs/watch', { watchId: subscriptionId, path: absolutePath })
    } catch (error) {
      this.forget(subscriptionId)
      throw error
    }
    return subscriptionId
  }

  unwatch(subscriptionId: string, senderId: number): void {
    const watch = this.watches.get(subscriptionId)
    if (!watch || watch.sender.id !== senderId) return
    this.forget(subscriptionId)
    void this.client.sendRequest('fs/unwatch', { watchId: subscriptionId }).catch(() => {})
  }

  private forget(subscriptionId: string): void {
    const watch = this.watches.get(subscriptionId)
    if (!watch) return
    watch.sender.removeListener('destroyed', watch.onDestroyed)
    this.watches.delete(subscriptionId)
    watchOwners.delete(subscriptionId)
  }

  private listen(): void {
    if (this.listening) return
    this.listening = true
    this.client.onNotification((method, params) => {
      if (method !== 'fs/changed') return
      const { watchId } = params as { watchId: string }
      if (this.watches.has(watchId)) void this.emitChange(watchId)
    })
    this.client.on('reconnected', () => {
      for (const [watchId, watch] of this.watches) {
        void this.client.sendRequest('fs/watch', { watchId, path: watch.absolutePath })
          .then(() => this.emitChange(watchId), () => {})
      }
    })
  }

  private async emitChange(subscriptionId: string): Promise<void> {
    const watch = this.watches.get(subscriptionId)
    if (!watch) return
    try {
      const metadata = await this.metadata(watch.absolutePath)
      const bytes = await this.readBytes(watch.absolutePath)
      if (watch.sender.isDestroyed() || !this.watches.has(subscriptionId)) return
      const payload: TextFileChangedPayload & { subscriptionId: string } = {
        subscriptionId,
        absolutePath: watch.absolutePath,
        mtimeMs: metadata.modifiedAtMs,
        sizeBytes: bytes.length
      }
      watch.sender.send('workspace:viewer:text-changed', payload)
    } catch {
      return
    }
  }

  private metadata(absolutePath: string): Promise<RemoteMetadata> {
    return this.client.sendRequest<RemoteMetadata>('fs/getMetadata', { path: absolutePath })
  }
}

const instances = new WeakMap<DesktopAppServerClient, RemoteViewerFiles>()

export function remoteViewerFilesFor(client: DesktopAppServerClient | null | undefined): RemoteViewerFiles | null {
  if (!client?.isRemoteHost) return null
  let files = instances.get(client)
  if (!files) instances.set(client, files = new RemoteViewerFiles(client))
  return files
}
