import { createHash } from 'crypto'
import { createReadStream, createWriteStream, existsSync } from 'fs'
import { mkdir, rename, rm, stat } from 'fs/promises'
import { dirname, join } from 'path'
import { Readable } from 'stream'

import type { VoiceModelState } from '../../shared/voice'
import {
  MANAGED_VOICE_MODEL,
  type ManagedVoiceModelDescriptor
} from './voiceModelDescriptor'

type FetchLike = typeof fetch
type ModelStateListener = (state: VoiceModelState) => void

export interface VoiceModelManagerOptions {
  voiceRoot: string
  fetchImpl?: FetchLike
  descriptor?: ManagedVoiceModelDescriptor
}

export class VoiceModelManager {
  readonly modelPath: string
  readonly partialPath: string

  private readonly fetchImpl: FetchLike
  private readonly descriptor: ManagedVoiceModelDescriptor
  private readonly listeners = new Set<ModelStateListener>()
  private state: VoiceModelState = { phase: 'missing', bytesDownloaded: 0, bytesTotal: null }
  private operation: Promise<void> | null = null
  private downloadAbort: AbortController | null = null

  constructor(options: VoiceModelManagerOptions) {
    this.fetchImpl = options.fetchImpl ?? fetch
    this.descriptor = options.descriptor ?? MANAGED_VOICE_MODEL
    this.modelPath = join(options.voiceRoot, 'models', this.descriptor.id, this.descriptor.fileName)
    this.partialPath = join(options.voiceRoot, 'downloads', `${this.descriptor.id}.part`)
  }

  getState(): VoiceModelState {
    return { ...this.state }
  }

  subscribe(listener: ModelStateListener): () => void {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }

  async initialize(): Promise<void> {
    await mkdir(dirname(this.modelPath), { recursive: true })
    await mkdir(dirname(this.partialPath), { recursive: true })
    if (!existsSync(this.modelPath)) {
      this.update({ phase: 'missing', bytesDownloaded: 0, bytesTotal: null })
      return
    }

    const valid = await this.verifyFile(this.modelPath)
    if (valid) {
      const file = await stat(this.modelPath)
      this.update({ phase: 'installed', bytesDownloaded: file.size, bytesTotal: file.size })
    } else {
      this.update({
        phase: 'damaged',
        bytesDownloaded: 0,
        bytesTotal: null,
        errorCode: 'model-damaged'
      })
    }
  }

  async install(): Promise<void> {
    if (this.state.phase === 'installed') return
    return this.runExclusive(async () => {
      const controller = new AbortController()
      this.downloadAbort = controller
      try {
        await mkdir(dirname(this.partialPath), { recursive: true })
        if (existsSync(this.partialPath) && await this.verifyFile(this.partialPath)) {
          await this.promotePartial()
          return
        }
        let offset = existsSync(this.partialPath) ? (await stat(this.partialPath)).size : 0
        const headers = offset > 0 ? { Range: `bytes=${offset}-` } : undefined
        const response = await this.fetchImpl(this.descriptor.downloadUrl, {
          headers,
          signal: controller.signal,
          redirect: 'follow'
        })
        if (!response.ok) throw new Error('download-failed')

        const resumed = offset > 0 && response.status === 206
        if (offset > 0 && !resumed) {
          await rm(this.partialPath, { force: true })
          offset = 0
        }
        const contentLength = parseContentLength(response.headers.get('content-length'))
        const bytesTotal = contentLength == null ? null : offset + contentLength
        this.update({ phase: 'downloading', bytesDownloaded: offset, bytesTotal })
        if (!response.body) throw new Error('download-failed')

        const output = createWriteStream(this.partialPath, { flags: resumed ? 'a' : 'w' })
        try {
          const readable = Readable.fromWeb(response.body as never)
          for await (const chunk of readable) {
            if (controller.signal.aborted) throw new DOMException('Cancelled', 'AbortError')
            const bytes = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk as Uint8Array)
            if (!output.write(bytes)) {
              await new Promise<void>((resolve) => output.once('drain', resolve))
            }
            offset += bytes.byteLength
            this.update({ phase: 'downloading', bytesDownloaded: offset, bytesTotal })
          }
        } finally {
          await new Promise<void>((resolve, reject) => {
            output.once('error', reject)
            output.end(resolve)
          })
        }

        if (!(await this.verifyFile(this.partialPath))) {
          this.update({
            phase: 'damaged',
            bytesDownloaded: offset,
            bytesTotal,
            errorCode: 'model-damaged'
          })
          return
        }

        await this.promotePartial()
      } catch (error) {
        if (isAbortError(error)) {
          this.update({ phase: 'missing', bytesDownloaded: 0, bytesTotal: null })
          return
        }
        const partialBytes = existsSync(this.partialPath) ? (await stat(this.partialPath)).size : 0
        this.update({
          phase: 'failed',
          bytesDownloaded: partialBytes,
          bytesTotal: this.state.bytesTotal,
          errorCode: 'download-failed'
        })
        throw new Error('download-failed')
      } finally {
        this.downloadAbort = null
      }
    })
  }

  async cancelInstall(): Promise<void> {
    this.downloadAbort?.abort()
    try {
      await this.operation
    } catch {
      // install() already published the safe failure state.
    }
    await rm(this.partialPath, { force: true })
    this.update({ phase: 'missing', bytesDownloaded: 0, bytesTotal: null })
  }

  async remove(): Promise<void> {
    this.downloadAbort?.abort()
    try {
      await this.operation
    } catch {
      // Removal is authoritative and continues after a failed install.
    }
    await Promise.all([
      rm(dirname(this.modelPath), { recursive: true, force: true }),
      rm(this.partialPath, { force: true })
    ])
    this.update({ phase: 'missing', bytesDownloaded: 0, bytesTotal: null })
  }

  async repair(): Promise<void> {
    await this.remove()
    await this.install()
  }

  private async runExclusive(operation: () => Promise<void>): Promise<void> {
    if (this.operation) return this.operation
    const running = operation()
    this.operation = running
    try {
      await running
    } finally {
      if (this.operation === running) this.operation = null
    }
  }

  private async verifyFile(path: string): Promise<boolean> {
    if (!existsSync(path)) return false
    const hash = createHash('sha256')
    for await (const chunk of createReadStream(path)) hash.update(chunk as Buffer)
    return hash.digest('hex') === this.descriptor.sha256
  }

  private async promotePartial(): Promise<void> {
    await mkdir(dirname(this.modelPath), { recursive: true })
    await rm(this.modelPath, { force: true })
    await rename(this.partialPath, this.modelPath)
    const installed = await stat(this.modelPath)
    this.update({ phase: 'installed', bytesDownloaded: installed.size, bytesTotal: installed.size })
  }

  private update(next: VoiceModelState): void {
    this.state = next
    for (const listener of this.listeners) listener({ ...next })
  }
}

function parseContentLength(value: string | null): number | null {
  if (!value) return null
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}
