import type { ChildProcess } from 'node:child_process'
import { EventEmitter } from 'node:events'
import { stat } from 'node:fs/promises'
import { randomUUID } from 'node:crypto'
import { resolve } from 'node:path'
import {
  devServerUrl,
  isActiveDevServer,
  type DevServerFailure,
  type DevServerList,
  type DevServerScope,
  type DevServerSnapshot,
  type DevServerStartResult,
  type DevServerStatus
} from '../../shared/devServers'
import { findConfiguration, launchFileState, loadLaunchFile, type CommandConfiguration } from './launchFile'
import { argsForPort, findFreePort, isListening, isPortInUse, scriptPinsPort } from './ports'
import { commandExists, killProcessTree, spawnServerProcess } from './serverProcess'
import { ServerLogBuffer } from './serverLogs'

export const MAX_SERVERS_PER_FOLDER = 5
const MAX_ATTEMPTS = 3
const READY_POLL_MS = 500
const READY_TIMEOUT_MS = 180_000
const MAX_ENDED_PER_THREAD = 20

interface ServerRecord {
  readonly id: string
  readonly name: string
  threadId: string
  readonly folder: string
  readonly folderKey: string
  readonly port: number
  readonly configuredPort?: number
  status: DevServerStatus
  readonly startedAt: string
  endedAt?: string
  failure?: DevServerFailure
  child?: ChildProcess
  readonly logs: ServerLogBuffer
}

export interface DevServerManagerOptions {
  readyTimeoutMs?: number
  readyPollMs?: number
}

function folderKeyOf(folder: string): string {
  const path = resolve(folder)
  return process.platform === 'win32' ? path.toLowerCase() : path
}

function snapshot(record: ServerRecord): DevServerSnapshot {
  return {
    serverId: record.id,
    name: record.name,
    threadId: record.threadId,
    folder: record.folder,
    port: record.port,
    ...(record.configuredPort === undefined ? {} : { configuredPort: record.configuredPort }),
    url: devServerUrl(record.port),
    status: record.status,
    startedAt: record.startedAt,
    ...(record.endedAt === undefined ? {} : { endedAt: record.endedAt }),
    ...(record.failure === undefined ? {} : { failure: record.failure })
  }
}

function lastLine(logs: ServerLogBuffer): string {
  return logs.lines().filter((line) => line.trim()).at(-1)?.trim() ?? ''
}

export class DevServerManager extends EventEmitter {
  private readonly records = new Map<string, ServerRecord>()
  private readonly starting = new Map<string, Promise<DevServerStartResult>>()
  private queue: Promise<unknown> = Promise.resolve()
  private readonly readyTimeoutMs: number
  private readonly readyPollMs: number

  constructor(options: DevServerManagerOptions = {}) {
    super()
    this.readyTimeoutMs = options.readyTimeoutMs ?? READY_TIMEOUT_MS
    this.readyPollMs = options.readyPollMs ?? READY_POLL_MS
  }

  async list(scope: DevServerScope): Promise<DevServerList> {
    const launch = launchFileState(await loadLaunchFile(scope.folder))
    return { folder: resolve(scope.folder), launch, servers: this.servers(scope) }
  }

  servers(scope: DevServerScope): DevServerSnapshot[] {
    return [...this.records.values()]
      .filter((record) => record.threadId === scope.threadId)
      .sort((a, b) => Date.parse(a.startedAt) - Date.parse(b.startedAt))
      .map(snapshot)
  }

  get(serverId: string): DevServerSnapshot | undefined {
    const record = this.records.get(serverId)
    return record ? snapshot(record) : undefined
  }

  logs(serverId: string): string[] {
    return this.records.get(serverId)?.logs.lines() ?? []
  }

  start(scope: DevServerScope, name: string): Promise<DevServerStartResult> {
    const key = `${folderKeyOf(scope.folder)}\n${scope.threadId}\n${name.trim().toLowerCase()}`
    const pending = this.starting.get(key)
    if (pending) return pending
    const work = this.queue.then(() => this.startOnce(scope, name)).finally(() => this.starting.delete(key))
    this.queue = work.catch(() => undefined)
    this.starting.set(key, work)
    return work
  }

  private async startOnce(scope: DevServerScope, name: string): Promise<DevServerStartResult> {
    const loaded = await loadLaunchFile(scope.folder)
    if (loaded.parsed.status !== 'ok') {
      const message = loaded.parsed.status === 'missing' ? 'This folder has no .craft/launch.json.' : loaded.parsed.reason
      return { ok: false, failure: { kind: 'launchFile', message } }
    }
    const configuration = findConfiguration(loaded.parsed.configurations, name)
    if (!configuration) {
      const names = loaded.parsed.configurations.map((entry) => entry.name).join(', ')
      return { ok: false, failure: { kind: 'launchFile', message: `No configuration named "${name}" (available: ${names}).` } }
    }
    if (configuration.kind === 'attach') return { ok: true, attachUrl: configuration.url }

    const folderKey = folderKeyOf(scope.folder)
    const existing = [...this.records.values()].find((record) =>
      record.folderKey === folderKey && record.threadId === scope.threadId &&
      record.name.toLowerCase() === configuration.name.toLowerCase())
    if (existing && isActiveDevServer(existing)) return { ok: true, server: snapshot(existing), reused: true }

    const active = [...this.records.values()].filter((record) => record.folderKey === folderKey && isActiveDevServer(record))
    if (active.length >= MAX_SERVERS_PER_FOLDER) {
      const otherThreads = active.filter((record) => record.threadId !== scope.threadId).length
      const message = `This folder already runs ${MAX_SERVERS_PER_FOLDER} dev servers, ${otherThreads} of them from other threads.`
      return { ok: false, failure: { kind: 'folderLimit', otherThreads, message } }
    }

    const otherPorts = new Set(loaded.parsed.configurations
      .filter((entry): entry is CommandConfiguration => entry.kind === 'command' && entry !== configuration)
      .map((entry) => entry.port))
    const placement = await this.placePort(scope, configuration, otherPorts)
    if (existing) this.records.delete(existing.id)
    const record = this.createRecord(scope, configuration.name, placement.port, configuration.port)
    if ('failure' in placement) {
      this.endRecord(record, 'error', placement.failure)
      return { ok: false, failure: placement.failure, server: snapshot(record) }
    }
    void this.run(record, configuration, placement.args)
    return { ok: true, server: snapshot(record), reused: false }
  }

  private async placePort(
    scope: DevServerScope,
    configuration: CommandConfiguration,
    otherPorts: ReadonlySet<number>
  ): Promise<{ port: number; args: string[] } | { port: number; failure: DevServerFailure }> {
    const { port } = configuration
    const heldBy = [...this.records.values()].find((record) => isActiveDevServer(record) && record.port === port)
    if (!heldBy && !(await isPortInUse(port))) return { port, args: [...configuration.args] }

    const movable = configuration.autoPort === true ||
      (configuration.autoPort === undefined &&
        !(await scriptPinsPort(configuration.command, configuration.args, configuration.cwd)))
    if (movable) {
      const avoid = new Set([...otherPorts, ...[...this.records.values()].filter(isActiveDevServer).map((record) => record.port)])
      const free = await findFreePort(port, avoid)
      if (free !== null) return { port: free, args: argsForPort(configuration.command, configuration.args, free) }
    }
    const holder = heldBy ? (heldBy.threadId === scope.threadId ? 'sameThread' : 'otherThread') : 'unknown'
    const user = {
      sameThread: `this thread's "${heldBy?.name}" server`,
      otherThread: `another thread's "${heldBy?.name}" server`,
      unknown: 'another process'
    }[holder]
    return { port, failure: { kind: 'portInUse', port, holder, message: `Port ${port} is in use by ${user}.` } }
  }

  private createRecord(scope: DevServerScope, name: string, port: number, configuredPort: number): ServerRecord {
    const record: ServerRecord = {
      id: randomUUID(),
      name,
      threadId: scope.threadId,
      folder: resolve(scope.folder),
      folderKey: folderKeyOf(scope.folder),
      port,
      ...(port === configuredPort ? {} : { configuredPort }),
      status: 'starting',
      startedAt: new Date().toISOString(),
      logs: new ServerLogBuffer()
    }
    this.records.set(record.id, record)
    this.pruneEnded(scope.threadId)
    this.emit('change')
    return record
  }

  private async run(record: ServerRecord, configuration: CommandConfiguration, args: string[]): Promise<void> {
    const cwdExists = await stat(configuration.cwd).then((info) => info.isDirectory(), () => false)
    if (!cwdExists) {
      this.endRecord(record, 'error', { kind: 'spawn', message: `The "cwd" folder ${configuration.cwd} does not exist.` })
      return
    }
    const env = {
      ...process.env,
      BROWSER: 'none',
      ...configuration.env,
      PORT: String(record.port)
    }
    if (!(await commandExists(configuration.command, configuration.cwd, env))) {
      this.endRecord(record, 'error', { kind: 'spawn', message: `"${configuration.command}" was not found on PATH.` })
      return
    }
    for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt += 1) {
      if (record.status !== 'starting') return
      if (attempt > 1) record.logs.append(`\n[DotCraft] Retrying (attempt ${attempt} of ${MAX_ATTEMPTS})\n`)
      const outcome = await this.attempt(record, configuration.command, args, configuration.cwd, env)
      if (outcome === 'ready' || outcome === 'stopped') return
      if (outcome.kind === 'spawn' || outcome.kind === 'neverReady' || attempt === MAX_ATTEMPTS) {
        this.endRecord(record, 'error', outcome)
        return
      }
    }
  }

  private attempt(
    record: ServerRecord,
    command: string,
    args: string[],
    cwd: string,
    env: NodeJS.ProcessEnv
  ): Promise<'ready' | 'stopped' | DevServerFailure> {
    return new Promise((settle) => {
      let child: ChildProcess
      try {
        child = spawnServerProcess({ command, args, cwd, env })
      } catch (error) {
        settle({ kind: 'spawn', message: `Could not start "${command}" (${(error as Error).message}).` })
        return
      }
      record.child = child
      let settled = false
      const finish = (result: 'ready' | 'stopped' | DevServerFailure): void => {
        if (settled) return
        settled = true
        clearTimeout(timeout)
        clearInterval(poll)
        settle(result)
      }
      child.stdout!.setEncoding('utf8').on('data', (chunk: string) => record.logs.append(chunk))
      child.stderr!.setEncoding('utf8').on('data', (chunk: string) => record.logs.append(chunk))
      child.once('error', (error: NodeJS.ErrnoException) => {
        finish({ kind: 'spawn', message: `Could not start "${command}" (${error.code ?? error.message}).` })
      })
      child.once('exit', (code) => {
        if (record.child === child) record.child = undefined
        void killProcessTree(child)
        if (record.status === 'stopped') {
          finish('stopped')
          return
        }
        if (record.status === 'running') {
          const detail = lastLine(record.logs)
          this.endRecord(record, 'error', { kind: 'exited', code, message: `The server exited${code === null ? '' : ` with code ${code}`}${detail ? `: ${detail}` : '.'}` })
          return
        }
        const detail = lastLine(record.logs)
        finish({ kind: 'exited', code, message: `The server exited before it was ready${code === null ? '' : ` (code ${code})`}${detail ? `: ${detail}` : '.'}` })
      })
      const timeout = setTimeout(() => {
        finish({ kind: 'neverReady', message: `Nothing answered on port ${record.port} within ${Math.round(this.readyTimeoutMs / 1000)} seconds.` })
        if (record.child === child) void killProcessTree(child)
      }, this.readyTimeoutMs)
      let probing = false
      const poll = setInterval(() => {
        if (probing || settled) return
        probing = true
        void isListening(record.port).then((listening) => {
          probing = false
          if (!listening || settled || record.status !== 'starting') return
          record.status = 'running'
          this.emit('change')
          finish('ready')
        })
      }, this.readyPollMs)
    })
  }

  private endRecord(record: ServerRecord, status: 'error' | 'stopped', failure?: DevServerFailure): void {
    if (record.status === 'stopped' || record.status === 'error') return
    record.status = status
    record.endedAt = new Date().toISOString()
    if (failure) {
      record.failure = failure
      record.logs.append(`\n[DotCraft] ${failure.message}\n`)
    }
    const child = record.child
    record.child = undefined
    if (child) void killProcessTree(child)
    this.emit('change')
  }

  async stop(serverId: string): Promise<DevServerSnapshot | undefined> {
    const record = this.records.get(serverId)
    if (!record) return undefined
    if (record.status === 'error') {
      record.status = 'stopped'
      this.emit('change')
    }
    if (!isActiveDevServer(record)) return snapshot(record)
    const child = record.child
    record.child = undefined
    record.status = 'stopped'
    record.endedAt = new Date().toISOString()
    this.emit('change')
    if (child) {
      await killProcessTree(child)
      this.emit('change')
    }
    return snapshot(record)
  }

  async stopScope(scope: DevServerScope): Promise<number> {
    const servers = this.servers(scope).filter(isActiveDevServer)
    await Promise.all(servers.map((server) => this.stop(server.serverId)))
    return servers.length
  }

  async forgetThread(threadId: string): Promise<void> {
    await this.queue
    const records = [...this.records.values()].filter((record) => record.threadId === threadId)
    await Promise.all(records.map((record) => this.stop(record.id)))
    for (const record of records) this.records.delete(record.id)
    if (records.length > 0) this.emit('change')
  }

  async stopFolder(folder: string): Promise<void> {
    await this.queue
    const key = folderKeyOf(folder)
    const records = [...this.records.values()].filter((record) => record.folderKey === key)
    await Promise.all(records.map((record) => this.stop(record.id)))
  }

  rebindThread(fromThreadId: string, toThreadId: string): void {
    let changed = false
    for (const record of this.records.values()) {
      if (record.threadId !== fromThreadId) continue
      record.threadId = toThreadId
      changed = true
    }
    if (changed) this.emit('change')
  }

  async disposeAll(): Promise<void> {
    await this.queue
    await Promise.all([...this.records.values()].map((record) => this.stop(record.id)))
  }

  private pruneEnded(threadId: string): void {
    const ended = [...this.records.values()]
      .filter((record) => record.threadId === threadId && !isActiveDevServer(record))
      .sort((a, b) => Date.parse(b.startedAt) - Date.parse(a.startedAt))
    for (const record of ended.slice(MAX_ENDED_PER_THREAD)) this.records.delete(record.id)
  }
}

export const devServerManager = new DevServerManager()
