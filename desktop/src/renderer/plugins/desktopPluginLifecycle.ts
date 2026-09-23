import type { DesktopPluginActivation } from '@dotcraft/plugin'

import type { PluginEntry } from '../stores/pluginStore'
import { showToast } from '../stores/toastStore'
import {
  publishDesktopPluginGeneration,
  withdrawDesktopPluginGeneration,
  type DesktopPluginGeneration
} from './desktopPluginRegistry'

import {
  callCleanup,
  createDesktopPluginHost,
  disposeDesktopPluginCleanupScope,
  installStyles,
  removeStyles,
  type DesktopPluginCleanupScope
} from './desktopPluginHost'
import { requireDesktopPluginModule, validateActivation } from './desktopPluginValidation'

interface DesktopPluginTarget {
  plugin: PluginEntry
  version: string
  revision: string
  rootPath: string
  sourceKey?: string
}

interface ActiveGeneration {
  generation: DesktopPluginGeneration
  activation: DesktopPluginActivation
  styles: HTMLLinkElement[]
  scope: DesktopPluginCleanupScope
  sourceKey?: string
}

interface PendingGeneration {
  invalidate(): Promise<void>
}

export interface DesktopPluginRuntimeDependencies {
  registerModule(params: {
    pluginId: string
    version: string
    revision: string
    rootPath: string
    sourceKey?: string
  }): Promise<{ entryUrl: string; styleUrls: string[] }>
  onError?(pluginId: string, error: unknown): void
  onActivated?(pluginId: string): void
  removeModule(params: { pluginId: string; revision: string; sourceKey?: string }): Promise<unknown>
  importModule(url: string): Promise<unknown>
}

export class DesktopPluginRuntime {
  private readonly active = new Map<string, ActiveGeneration>()
  private readonly desired = new Map<string, DesktopPluginTarget>()
  private readonly tokens = new Map<string, number>()
  private readonly pending = new Map<string, PendingGeneration>()
  private stopped = false

  constructor(private readonly dependencies: DesktopPluginRuntimeDependencies) { }

  reconcile(plugins: readonly PluginEntry[], sourceKey?: string): void {
    if (this.stopped) return
    const next = new Map<string, DesktopPluginTarget>()
    for (const plugin of plugins) {
      if (
        !plugin.installed
        || !plugin.enabled
        || !plugin.desktop
        || !plugin.version
        || typeof plugin.desktop.revision !== 'string'
        || !plugin.desktop.revision
      ) continue
      next.set(plugin.id.toLowerCase(), {
        plugin,
        version: plugin.version,
        revision: plugin.desktop.revision,
        sourceKey,
        rootPath: plugin.rootPath
      })
    }

    const ids = new Set([...this.desired.keys(), ...next.keys()])
    for (const pluginId of ids) {
      const previous = this.desired.get(pluginId)
      const target = next.get(pluginId)
      if (sameTarget(previous, target)) continue
      if (target) this.desired.set(pluginId, target)
      else this.desired.delete(pluginId)
      const token = (this.tokens.get(pluginId) ?? 0) + 1
      this.tokens.set(pluginId, token)
      const pendingTeardown = this.invalidatePending(pluginId)
      this.scheduleReplace(pluginId, target ?? null, token, pendingTeardown)
    }
  }

  async stop(): Promise<void> {
    if (this.stopped) return
    this.stopped = true
    this.desired.clear()
    for (const pluginId of new Set([...this.tokens.keys(), ...this.active.keys()])) {
      this.tokens.set(pluginId, (this.tokens.get(pluginId) ?? 0) + 1)
    }
    await Promise.all([
      ...[...this.pending.keys()].map((pluginId) => this.invalidatePending(pluginId)),
      ...[...this.active.keys()].map((pluginId) => this.deactivate(pluginId))
    ])
  }

  private scheduleReplace(
    pluginId: string,
    target: DesktopPluginTarget | null,
    token: number,
    pendingTeardown: Promise<void>
  ): void {
    void this.replace(pluginId, target, token, pendingTeardown)
  }

  private async replace(
    pluginId: string,
    target: DesktopPluginTarget | null,
    token: number,
    pendingTeardown: Promise<void>
  ): Promise<void> {
    await pendingTeardown
    if (!this.current(pluginId, token, target)) return
    await this.deactivate(pluginId)
    if (!this.current(pluginId, token, target) || !target) return

    const plugin = target.plugin
    let routeRegistered = false
    let styles: HTMLLinkElement[] = []
    let activation: DesktopPluginActivation | null = null
    let published = false
    const scope: DesktopPluginCleanupScope = { active: true, cleanups: new Set() }
    let pending: PendingGeneration | null = null
    try {
      const routePromise = this.dependencies.registerModule({
        pluginId,
        version: plugin.version!,
        revision: target.revision,
        rootPath: plugin.rootPath,
        sourceKey: target.sourceKey
      }).then((route) => {
        routeRegistered = true
        return route
      })

      let invalidation: Promise<void> | null = null
      pending = {
        invalidate: () => {
          disposeDesktopPluginCleanupScope(scope)
          invalidation ??= routePromise.then(async () => {
            if (!routeRegistered) return
            routeRegistered = false
            await this.removeModule(pluginId, target.revision, target.sourceKey)
          }, () => { })
          return invalidation
        }
      }
      this.pending.set(pluginId, pending)

      const route = await routePromise
      if (!this.current(pluginId, token, target)) return

      const host = createDesktopPluginHost(plugin, pluginId, target.revision, scope)
      const imported = await this.dependencies.importModule(route.entryUrl)
      if (!this.current(pluginId, token, target)) return
      const module = requireDesktopPluginModule(imported)
      activation = (await module.activate(host)) ?? {}
      const generation = validateActivation(pluginId, target.version, target.revision, host, activation)
      if (!this.current(pluginId, token, target)) return

      styles = installStyles(pluginId, target.revision, route.styleUrls)
      publishDesktopPluginGeneration(generation)
      if (this.pending.get(pluginId) === pending) this.pending.delete(pluginId)
      this.active.set(pluginId, { generation, activation, styles, scope, sourceKey: target.sourceKey })
      published = true
      activation = null
      styles = []
      routeRegistered = false
      this.dependencies.onActivated?.(pluginId)
    } catch (error) {
      if (this.current(pluginId, token, target)) {
        console.error(`Desktop Plugin '${pluginId}' activation failed:`, error)
        this.dependencies.onError?.(pluginId, error)
        showToast({
          message: error instanceof Error ? error.message : String(error),
          type: 'error'
        })
      }
    } finally {
      if (!published) {
        if (activation?.dispose) void callCleanup(activation.dispose)
        disposeDesktopPluginCleanupScope(scope)
        removeStyles(styles)
        if (pending) await pending.invalidate()
        else if (routeRegistered) {
          routeRegistered = false
          await this.removeModule(pluginId, target.revision, target.sourceKey)
        }
      }
      if (pending && this.pending.get(pluginId) === pending) this.pending.delete(pluginId)
    }
  }

  private async deactivate(pluginId: string): Promise<void> {
    const active = this.active.get(pluginId)
    if (!active) return
    this.active.delete(pluginId)
    withdrawDesktopPluginGeneration(pluginId)
    if (active.activation.dispose) void callCleanup(active.activation.dispose)
    disposeDesktopPluginCleanupScope(active.scope)
    removeStyles(active.styles)
    await this.removeModule(pluginId, active.generation.revision, active.sourceKey)
  }

  private invalidatePending(pluginId: string): Promise<void> {
    const pending = this.pending.get(pluginId)
    if (!pending) return Promise.resolve()
    const teardown = pending.invalidate()
    void teardown.then(() => {
      if (this.pending.get(pluginId) === pending) this.pending.delete(pluginId)
    })
    return teardown
  }

  private async removeModule(pluginId: string, revision: string, sourceKey?: string): Promise<void> {
    await this.dependencies.removeModule({ pluginId, revision, ...(sourceKey ? { sourceKey } : {}) }).catch((error: unknown) => {
      console.error(`Desktop Plugin '${pluginId}' module cleanup failed:`, error)
    })
  }

  private current(pluginId: string, token: number, target: DesktopPluginTarget | null): boolean {
    return !this.stopped
      && this.tokens.get(pluginId) === token
      && sameTarget(this.desired.get(pluginId), target ?? undefined)
  }
}

function sameTarget(
  left: DesktopPluginTarget | undefined,
  right: DesktopPluginTarget | undefined
): boolean {
  return left?.version === right?.version
    && left?.revision === right?.revision
    && left?.rootPath === right?.rootPath
    && left?.sourceKey === right?.sourceKey
}
