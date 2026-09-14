import { randomUUID } from 'node:crypto'
import { existsSync, mkdirSync, readFileSync, writeFileSync, renameSync } from 'node:fs'
import { basename, dirname, extname, join } from 'node:path'
import type { DownloadItem } from 'electron'
import type { BrowserDownloadRecord } from '../shared/viewer/browserFeedback'

export class BrowserDownloads {
  private records: BrowserDownloadRecord[] = []
  private readonly items = new Map<string, DownloadItem>()

  constructor(
    private directory: string,
    private readonly recordPath: string,
    private readonly changed: (records: BrowserDownloadRecord[]) => void,
    private readonly openPath: (path: string) => Promise<string>
  ) {
    const locationPath = `${recordPath}.location`
    if (existsSync(locationPath)) this.directory = readFileSync(locationPath, 'utf8')
    if (!existsSync(recordPath)) return
    const stored: unknown = JSON.parse(readFileSync(recordPath, 'utf8'))
    if (!Array.isArray(stored)) return
    this.records = stored.filter((value): value is BrowserDownloadRecord =>
      value != null && typeof value.id === 'string' && typeof value.path === 'string' &&
      typeof value.filename === 'string' && typeof value.tabId === 'string'
    ).map((record) => ({ ...record, state: record.state === 'progressing' ? 'interrupted' : record.state }))
    this.persist()
  }

  snapshot(): BrowserDownloadRecord[] {
    return this.records.map((record) => ({ ...record }))
  }

  start(item: DownloadItem, origin: { tabId: string; threadId?: string }): void {
    mkdirSync(this.directory, { recursive: true })
    const original = basename(item.getFilename()) || 'download'
    const extension = extname(original)
    const stem = original.slice(0, original.length - extension.length)
    let filename = original
    let suffix = 1
    const reserved = new Set(this.records.filter((record) => record.state === 'progressing').map((record) => record.path))
    while (existsSync(join(this.directory, filename)) || reserved.has(join(this.directory, filename))) {
      filename = `${stem} (${suffix++})${extension}`
    }
    const record: BrowserDownloadRecord = {
      id: randomUUID(), ...origin, filename, url: item.getURL(), path: join(this.directory, filename),
      receivedBytes: 0, totalBytes: item.getTotalBytes(), state: 'progressing', startedAt: Date.now()
    }
    item.setSavePath(record.path)
    this.records.unshift(record)
    this.items.set(record.id, item)
    item.on('updated', (_event, state) => {
      record.receivedBytes = item.getReceivedBytes()
      record.totalBytes = item.getTotalBytes()
      record.state = state === 'interrupted' ? 'interrupted' : 'progressing'
      this.publish()
    })
    item.once('done', (_event, state) => {
      record.state = state
      record.receivedBytes = item.getReceivedBytes()
      record.totalBytes = item.getTotalBytes()
      this.items.delete(record.id)
      this.publish()
    })
    this.publish()
  }

  location(): string { return this.directory }

  setLocation(directory: string): void {
    mkdirSync(dirname(this.recordPath), { recursive: true })
    writeFileSync(`${this.recordPath}.location`, directory, 'utf8')
    this.directory = directory
  }

  remove(id?: string): void {
    this.records = this.records.filter(record => this.items.has(record.id) || (id !== undefined && record.id !== id))
    this.publish()
  }

  cancel(id: string): void { this.items.get(id)?.cancel() }

  async open(id: string): Promise<void> {
    const record = this.records.find((candidate) => candidate.id === id)
    if (!record || record.state !== 'completed') throw new Error('Download is not complete.')
    const error = await this.openPath(record.path)
    if (error) throw new Error(error)
  }

  private persist(): void {
    mkdirSync(dirname(this.recordPath), { recursive: true })
    const temporary = `${this.recordPath}.tmp`
    writeFileSync(temporary, JSON.stringify(this.records), 'utf8')
    renameSync(temporary, this.recordPath)
  }

  private publish(): void {
    this.persist()
    this.changed(this.snapshot())
  }
}
