import { mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs'
import { dirname } from 'node:path'
import { readAppServerErrorFields } from '../../shared/appServerError'
import { STANDALONE_CHAT_INSTRUCTIONS } from '../../shared/chatFolders'
import type { OwnedThreadConfig, OwnedThreadRecord, OwnedThreadRequest } from '../../shared/ownedThreads'
import { withTaskListInstructions } from '../../shared/taskListInstructions'
import { desktopIdentity } from './childThreads'

export interface OwnedThreadConnection {
  workspacePath: string
  request<T = unknown>(method: string, params?: unknown): Promise<T>
  createChatFolder(firstMessage: string): Promise<string>
}

interface ThreadReadResult {
  thread?: { id?: string; status?: string; configuration?: Record<string, unknown> | null }
}

export class DesktopOwnedThreads {
  private readonly records = new Map<string, OwnedThreadRecord>()
  private readonly pending = new Map<string, Promise<OwnedThreadRecord>>()

  constructor(private readonly filePath: string | null, private readonly onChanged?: (records: OwnedThreadRecord[]) => void) {
    if (!filePath) return
    try {
      const { records } = JSON.parse(readFileSync(filePath, 'utf8')) as { records: OwnedThreadRecord[] }
      for (const record of records) {
        if (record.pluginId && record.key && record.threadId) this.records.set(recordKey(record), record)
      }
    } catch {
      this.records.clear()
    }
  }

  list(): OwnedThreadRecord[] {
    return [...this.records.values()].map((record) => ({ ...record }))
  }

  findByThreadId(threadId: string): OwnedThreadRecord | undefined {
    for (const record of this.records.values()) {
      if (record.threadId === threadId) return { ...record }
    }
    return undefined
  }

  isPaused(threadId: string): boolean {
    for (const record of this.records.values()) {
      if (record.threadId === threadId) return record.paused === true
    }
    return false
  }

  setPaused(threadId: string, paused: boolean): OwnedThreadRecord | null {
    for (const record of this.records.values()) {
      if (record.threadId !== threadId) continue
      const next: OwnedThreadRecord = paused
        ? { ...record, paused: true }
        : stripPaused(record)
      return this.save(next)
    }
    return null
  }

  removeByThreadId(threadId: string): OwnedThreadRecord | null {
    for (const [key, record] of this.records.entries()) {
      if (record.threadId !== threadId) continue
      this.records.delete(key)
      this.persist()
      this.onChanged?.(this.list())
      return { ...record }
    }
    return null
  }

  ensure(request: OwnedThreadRequest, connection: OwnedThreadConnection): Promise<OwnedThreadRecord> {
    const key = recordKey(request)
    const existing = this.pending.get(key)
    if (existing) return existing
    const work = this.resolve(request, connection).finally(() => this.pending.delete(key))
    this.pending.set(key, work)
    return work
  }

  private async resolve(request: OwnedThreadRequest, connection: OwnedThreadConnection): Promise<OwnedThreadRecord> {
    const fingerprint = JSON.stringify(request.config)
    const record = this.records.get(recordKey(request))
    if (record && record.workspacePath === connection.workspacePath) {
      const thread = await readThread(connection, record.threadId)
      if (thread && thread.status !== 'archived') {
        if (record.fingerprint !== fingerprint) {
          await connection.request('thread/config/update', {
            threadId: record.threadId,
            config: { ...(thread.configuration ?? {}), ...ownedConfig(request.config) }
          })
        }
        return this.save({ ...record, space: request.space ?? null, title: request.title, fingerprint })
      }
    }

    const result = await connection.request<ThreadReadResult>('thread/start', {
      identity: desktopIdentity(connection.workspacePath),
      historyMode: 'server',
      displayName: request.title,
      cwd: await connection.createChatFolder(request.title),
      config: ownedConfig(request.config)
    })
    const threadId = result.thread?.id
    if (!threadId) throw new Error('thread/start returned no thread.')
    return this.save({
      pluginId: request.pluginId,
      key: request.key,
      space: request.space ?? null,
      title: request.title,
      threadId,
      workspacePath: connection.workspacePath,
      fingerprint
    })
  }

  private save(record: OwnedThreadRecord): OwnedThreadRecord {
    const previous = this.records.get(recordKey(record))
    this.records.set(recordKey(record), record)
    if (JSON.stringify(previous) === JSON.stringify(record)) return { ...record }
    this.persist()
    this.onChanged?.(this.list())
    return { ...record }
  }

  private persist(): void {
    if (!this.filePath) return
    try {
      mkdirSync(dirname(this.filePath), { recursive: true })
      const temporary = `${this.filePath}.tmp`
      writeFileSync(temporary, JSON.stringify({ records: this.list() }), 'utf8')
      renameSync(temporary, this.filePath)
    } catch (error) {
      console.warn('[ownedThreads] failed to save owned threads', error)
    }
  }
}

function stripPaused(record: OwnedThreadRecord): OwnedThreadRecord {
  const { paused: _ignored, ...rest } = record
  return rest
}

function ownedConfig(config: OwnedThreadConfig): Record<string, unknown> {
  return {
    developerInstructions: `${withTaskListInstructions(STANDALONE_CHAT_INSTRUCTIONS)}\n\n${config.developerInstructions}`,
    ...(config.approvalPolicy ? { approvalPolicy: config.approvalPolicy } : {})
  }
}

async function readThread(connection: OwnedThreadConnection, threadId: string): Promise<ThreadReadResult['thread'] | null> {
  try {
    const { thread } = await connection.request<ThreadReadResult>('thread/read', { threadId })
    return thread ?? null
  } catch (error) {
    const { rpcCode, data } = readAppServerErrorFields(error)
    if (data?.code === 'ThreadNotFound' || rpcCode === -32010) return null
    throw error
  }
}

function recordKey({ pluginId, key }: Pick<OwnedThreadRecord, 'pluginId' | 'key'>): string {
  return JSON.stringify([pluginId, key])
}
