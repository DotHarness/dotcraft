import { promises as fs } from 'fs'
import * as path from 'path'
import type { DesktopAppServerClient } from './DesktopAppServerClient'
import type {
  DiscoveredModule,
  ModuleProcessState,
  ModuleStatusMap,
  QrUpdatePayload
} from '../shared/channelModules'
export type { ModuleStatusEntry, ModuleStatusMap } from '../shared/channelModules'
import { QrFileWatcher } from './qrWatcher'

interface ManagedModuleProcess {
  moduleId: string
  channelName: string
  state: ModuleProcessState
  failureCode: string | null
}

interface ChannelStatusWire {
  name: string
  enabled: boolean
  running: boolean
  runtimeState?: string
  failureCode?: string
}

interface StartResult {
  ok: boolean
  error?: string
}

interface StopResult {
  ok: boolean
  error?: string
}

const POLL_INTERVAL_MS = 3_000
const LOG_TAIL_LINES = 100
function builtinModuleName(module: DiscoveredModule): string {
  return path.basename(module.absolutePath)
}

function externalChannelTransportForModule(module: DiscoveredModule): 'managedWebsocket' | 'subprocess' {
  const transports = module.supportedTransports.map((transport) => transport.trim().toLowerCase())
  return transports.includes('websocket') ? 'managedWebsocket' : 'subprocess'
}

export class ChannelModuleManager {
  private readonly workspacePath: string
  private readonly getWireClient: () => DesktopAppServerClient | null
  private readonly onStatusChanged: (statusMap: ModuleStatusMap) => void
  private readonly getCachedModules: () => DiscoveredModule[] | null
  private readonly qrWatcher: QrFileWatcher
  private readonly managed = new Map<string, ManagedModuleProcess>()
  private statusPollTimer: ReturnType<typeof setInterval> | null = null
  private lastPolledConnected = new Map<string, boolean>()
  private lastBroadcastSnapshot = ''

  constructor(options: {
    workspacePath: string
    getWireClient: () => DesktopAppServerClient | null
    onStatusChanged: (statusMap: ModuleStatusMap) => void
    getCachedModules: () => DiscoveredModule[] | null
    onQrUpdate: (payload: QrUpdatePayload) => void
  }) {
    this.workspacePath = options.workspacePath
    this.getWireClient = options.getWireClient
    this.onStatusChanged = options.onStatusChanged
    this.getCachedModules = options.getCachedModules
    this.qrWatcher = new QrFileWatcher({
      workspacePath: options.workspacePath,
      onQrUpdate: options.onQrUpdate
    })
  }

  async start(moduleId: string): Promise<StartResult> {
    const module = this.findModule(moduleId)
    if (!module) {
      return { ok: false, error: `Module '${moduleId}' not found` }
    }

    try {
      await fs.access(path.join(this.workspacePath, '.craft', module.configFileName))
    } catch {
      return { ok: false, error: `Missing config file: ${module.configFileName}` }
    }

    const upsert = await this.upsertExternalChannel(module, true)
    if (!upsert.ok) return upsert

    await this.trackModules([moduleId])
    return { ok: true }
  }

  async stop(moduleId: string): Promise<StopResult> {
    const module = this.findModule(moduleId)
    const entry = this.managed.get(moduleId)
    if (!module && !entry) {
      return { ok: true }
    }

    if (entry) {
      entry.state = 'stopping'
      this.emitStatusIfChanged()
    }

    const targetModule = module ?? (entry ? this.findModule(entry.moduleId) : null)
    if (targetModule) {
      const disabled = await this.upsertExternalChannel(targetModule, false)
      if (!disabled.ok) return disabled
    }

    if (entry) {
      entry.state = 'stopped'
      entry.failureCode = null
      this.lastPolledConnected.set(moduleId, false)
    }
    this.qrWatcher.stopWatching(moduleId)
    this.stopPollerIfIdle()
    this.emitStatusIfChanged()
    return { ok: true }
  }

  dispose(): void {
    for (const entry of this.managed.values()) {
      this.qrWatcher.stopWatching(entry.moduleId)
    }
    this.managed.clear()
    this.lastPolledConnected.clear()
    if (this.statusPollTimer) {
      clearInterval(this.statusPollTimer)
      this.statusPollTimer = null
    }
    this.emitStatusIfChanged()
  }

  getStatusMap(): ModuleStatusMap {
    const status: ModuleStatusMap = {}
    for (const [moduleId, entry] of this.managed) {
      status[moduleId] = {
        processState: entry.state,
        connected: this.lastPolledConnected.get(moduleId) ?? false,
        failureCode: entry.failureCode ?? undefined
      }
    }
    return status
  }

  async getRecentLogs(moduleId: string): Promise<string[]> {
    const module = this.findModule(moduleId)
    const entry = this.managed.get(moduleId)
    const channelName = module?.channelName ?? entry?.channelName
    if (!channelName) return []

    const client = this.getWireClient()
    if (!client) return []
    try {
      const response = await client.sendRequest<{ lines?: string[] }>('externalChannel/logs', {
        name: channelName,
        tail: LOG_TAIL_LINES
      })
      return response.lines ?? []
    } catch {
      return []
    }
  }

  getQrStatus(moduleId: string): { active: boolean; qrDataUrl: string | null } {
    return this.qrWatcher.getStatus(moduleId)
  }

  async restoreModules(enabledIds: string[]): Promise<void> {
    const enabled = new Set(enabledIds)
    for (const moduleId of this.managed.keys()) {
      if (enabled.has(moduleId)) continue
      this.managed.delete(moduleId)
      this.lastPolledConnected.delete(moduleId)
      this.qrWatcher.stopWatching(moduleId)
    }
    await this.trackModules(enabledIds)
  }

  private async trackModules(moduleIds: string[]): Promise<void> {
    for (const moduleId of moduleIds) {
      const module = this.findModule(moduleId)
      if (!module) continue
      this.managed.set(moduleId, {
        moduleId: module.moduleId,
        channelName: module.channelName,
        state: 'starting',
        failureCode: null
      })
      this.lastPolledConnected.set(moduleId, false)
      if (module.requiresInteractiveSetup) {
        void this.qrWatcher.startWatching(module.moduleId)
      }
    }
    if (this.managed.size === 0) {
      this.stopPollerIfIdle()
      this.emitStatusIfChanged()
      return
    }
    this.ensurePoller()
    await this.pollChannelStatus()
    this.emitStatusIfChanged()
  }

  private async upsertExternalChannel(module: DiscoveredModule, enabled: boolean): Promise<StartResult> {
    const client = this.getWireClient()
    if (!client) {
      return { ok: false, error: 'AppServer is not connected' }
    }

    try {
      await client.sendRequest('externalChannel/upsert', {
        channel: {
          name: module.channelName,
          enabled,
          transport: externalChannelTransportForModule(module),
          builtinModule: builtinModuleName(module)
        }
      })
      return { ok: true }
    } catch (error) {
      return {
        ok: false,
        error: error instanceof Error ? error.message : String(error)
      }
    }
  }

  private findModule(moduleId: string): DiscoveredModule | null {
    const cached = this.getCachedModules()
    if (!cached) return null
    return cached.find((item) => item.moduleId === moduleId) ?? null
  }

  private ensurePoller(): void {
    if (this.statusPollTimer) return
    this.statusPollTimer = setInterval(() => {
      void this.pollChannelStatus()
    }, POLL_INTERVAL_MS)
    void this.pollChannelStatus()
  }

  private stopPollerIfIdle(): void {
    const active = [...this.managed.values()].some(
      (entry) => entry.state === 'starting' || entry.state === 'running'
    )
    if (!active && this.statusPollTimer) {
      clearInterval(this.statusPollTimer)
      this.statusPollTimer = null
    }
  }

  private async pollChannelStatus(): Promise<void> {
    const activeEntries = [...this.managed.values()].filter(
      (entry) => entry.state === 'starting' || entry.state === 'running'
    )
    if (activeEntries.length === 0) {
      this.stopPollerIfIdle()
      return
    }

    const client = this.getWireClient()
    if (!client) {
      for (const entry of activeEntries) {
        this.lastPolledConnected.set(entry.moduleId, false)
      }
      this.emitStatusIfChanged()
      return
    }

    try {
      const response = await client.sendRequest<{ channels?: ChannelStatusWire[] }>(
        'channel/status',
        {}
      )
      const channels = new Map<string, ChannelStatusWire>()
      for (const channel of response.channels ?? []) {
        channels.set(channel.name.toLowerCase(), channel)
      }

      for (const entry of activeEntries) {
        const status = channels.get(entry.channelName.toLowerCase())
        const wasConnected = this.lastPolledConnected.get(entry.moduleId) ?? false
        const isConnected = status?.running === true
        this.lastPolledConnected.set(entry.moduleId, isConnected)
        entry.failureCode = status?.failureCode ?? null
        switch (status?.runtimeState) {
          case 'failed':
            entry.state = 'crashed'
            break
          case 'stopped':
            entry.state = 'stopped'
            break
          case 'running':
            entry.state = 'running'
            break
          case 'starting':
            entry.state = 'starting'
            break
          default:
            entry.state = isConnected ? 'running' : 'starting'
            break
        }

        const module = this.findModule(entry.moduleId)
        if (module?.requiresInteractiveSetup) {
          if (isConnected && !wasConnected) {
            this.qrWatcher.onChannelConnected(entry.moduleId)
          } else if (!isConnected && wasConnected) {
            this.qrWatcher.onChannelDisconnected(entry.moduleId)
          }
        }

      }
      this.emitStatusIfChanged()
      this.stopPollerIfIdle()
    } catch {
      for (const entry of activeEntries) {
        this.lastPolledConnected.set(entry.moduleId, false)
      }
      this.emitStatusIfChanged()
    }
  }

  private emitStatusIfChanged(): void {
    const statusMap = this.getStatusMap()
    const snapshot = JSON.stringify(statusMap)
    if (snapshot === this.lastBroadcastSnapshot) return
    this.lastBroadcastSnapshot = snapshot
    this.onStatusChanged(statusMap)
  }
}
