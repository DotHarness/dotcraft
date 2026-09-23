import { DesktopPluginArtifactCache, desktopPluginSourceKey, type ArtifactReader } from './desktopPluginArtifactCache'
import {
  registerDesktopPluginModuleRoute, removeDesktopPluginModuleRoute,
  type DesktopPluginModuleRequest, type DesktopPluginModuleRoute
} from './pluginFileProtocol'
import type { DesktopPluginSource } from '../shared/desktopPluginSource'
import type { PluginListResult } from '@dotcraft/sdk/contracts'

export interface DesktopPluginConnection {
  identity: object
  remote: boolean
  source: string
  workspacePath: string
  supported: boolean
  list(): Promise<PluginListResult>
  read: ArtifactReader
}

export class DesktopPluginModules {
  private queue: Promise<unknown> = Promise.resolve()
  private disposed = false
  private epoch = 0
  private active = new Map<string, DesktopPluginModuleRequest>()
  private snapshot: PluginListResult | null = null
  private source: DesktopPluginSource | null = null
  private connectionIdentity: object | null = null

  constructor(private readonly options: {
    connection(): DesktopPluginConnection
    cache: DesktopPluginArtifactCache
    grants(): readonly string[]
    saveGrants(grants: string[]): Promise<void>
  }) {}

  async context(): Promise<DesktopPluginSource> {
    const connection = this.options.connection()
    const snapshot = connection.remote && connection.supported ? await connection.list() : null
    if (this.disposed || this.options.connection().identity !== connection.identity) throw new Error('Desktop plugin source changed.')
    const workspacePath = snapshot?.workspacePath || connection.workspacePath
    if (connection.remote && connection.supported && !snapshot?.workspacePath) throw new Error('Remote plugin workspace identity is missing.')
    const sourceKey = desktopPluginSourceKey(connection.source, workspacePath)
    const source = {
      sourceKey, workspacePath, remote: connection.remote, supported: connection.supported,
      trusted: !connection.remote || (connection.supported && this.options.grants().includes(sourceKey))
    }
    if (this.source?.sourceKey !== sourceKey || this.connectionIdentity !== connection.identity) {
      this.epoch++
      this.clearRoutes()
    }
    this.snapshot = snapshot
    this.source = source
    this.connectionIdentity = connection.identity
    await this.prune()
    return source
  }

  async setTrusted(sourceKey: string, trusted: boolean): Promise<DesktopPluginSource> {
    if (typeof trusted !== 'boolean') throw new Error('Invalid Desktop plugin authorization.')
    const context = await this.context()
    if (!context.remote || !context.supported || context.sourceKey !== sourceKey) throw new Error('Desktop plugin source changed.')
    const grants = this.options.grants().filter(key => key !== sourceKey)
    if (trusted) grants.push(sourceKey)
    await this.options.saveGrants(grants)
    if (!trusted) {
      this.epoch++
      this.clearRoutes()
    }
    return { ...context, trusted }
  }

  register(request: DesktopPluginModuleRequest): Promise<DesktopPluginModuleRoute> {
    const epoch = this.epoch
    const connection = this.options.connection()
    const job = this.queue.catch(() => {}).then(async () => {
      if (connection.remote && !connection.supported) throw new Error('Remote server does not support Desktop plugin artifacts.')
      const current = () => !this.disposed && this.epoch === epoch
        && this.options.connection().identity === connection.identity
        && (!connection.remote || this.options.grants().includes(request.sourceKey ?? ''))
      if (!current() || this.source?.sourceKey !== request.sourceKey) throw new Error('Desktop plugin source changed or is not authorized.')
      let rootPath = request.rootPath
      if (connection.remote) {
        rootPath = await this.options.cache.prepare(request.sourceKey!, request, connection.read, current)
        const snapshot = await connection.list()
        this.snapshot = snapshot
        if (snapshot.workspacePath !== this.source?.workspacePath || !snapshot.plugins?.some(plugin =>
          plugin.id?.toLowerCase() === request.pluginId.toLowerCase() && plugin.installed && plugin.enabled
          && plugin.desktop?.revision === request.revision)) throw new Error('Remote Desktop plugin changed. Refresh the plugin list.')
      }
      if (!current()) throw new Error('Desktop plugin source changed.')
      const route: DesktopPluginModuleRoute = await registerDesktopPluginModuleRoute({ ...request, rootPath }, {
        cached: connection.remote
      })
      if (!current()) {
        removeDesktopPluginModuleRoute(request.pluginId, request.revision, request.sourceKey)
        throw new Error('Desktop plugin source changed.')
      }
      this.active.set(routeKey(request), request)
      await this.prune()
      return route
    })
    this.queue = job
    return job
  }

  async remove(request: Pick<DesktopPluginModuleRequest, 'pluginId' | 'revision' | 'sourceKey'>): Promise<void> {
    removeDesktopPluginModuleRoute(request.pluginId, request.revision, request.sourceKey)
    this.active.delete(routeKey(request))
    await this.prune()
  }

  dispose(): void {
    this.disposed = true
    this.epoch++
    this.clearRoutes()
  }

  private clearRoutes(): void {
    for (const request of this.active.values()) removeDesktopPluginModuleRoute(request.pluginId, request.revision, request.sourceKey)
    this.active.clear()
  }

  private async prune(): Promise<void> {
    if (!this.source?.remote || !this.source.supported || !this.snapshot) return
    const keep = new Map<string, Set<string>>()
    const add = (id: string, revision: string) => {
      const revisions = keep.get(id.toLowerCase()) ?? new Set<string>()
      revisions.add(revision)
      keep.set(id.toLowerCase(), revisions)
    }
    for (const plugin of this.snapshot.plugins ?? []) {
      if (plugin.id && plugin.installed && plugin.desktop?.revision) add(plugin.id, plugin.desktop.revision)
    }
    for (const request of this.active.values()) {
      if (request.sourceKey === this.source.sourceKey) add(request.pluginId, request.revision)
    }
    await this.options.cache.prune(this.source.sourceKey, keep).catch(error => console.error('Desktop plugin cache cleanup failed:', error))
  }
}

function routeKey(request: Pick<DesktopPluginModuleRequest, 'pluginId' | 'revision' | 'sourceKey'>): string {
  return `${request.sourceKey}:${request.pluginId}:${request.revision}`
}
