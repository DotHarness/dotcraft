import type {
  DesktopPluginActivation,
  DesktopPluginActivate,
  DesktopPluginCommandContribution,
  DesktopPluginConversationViewContribution,
  DesktopPluginHost,
  DesktopPluginMainViewContribution,
  DesktopPluginMessageActionContribution,
  DesktopPluginSettingsPageContribution,
  DesktopPluginToolRendererContribution
} from '@dotcraft/plugin'
import { installDesktopPluginRuntime as installAuthoringRuntime } from '@dotcraft/plugin/runtime'
import type {
  AppConnectionStartResult,
  AppConnectionStatusResult,
  ClientRequestMethods,
  ServerNotificationMethods
} from '@dotcraft/sdk/contracts'
import * as React from 'react'
import * as JsxRuntime from 'react/jsx-runtime'
import { createPortal } from 'react-dom'

import { Button } from '../components/ui/Button'
import { ActionTooltip } from '../components/ui/ActionTooltip'
import { Checkbox } from '../components/ui/Checkbox'
import { Combobox } from '../components/ui/Combobox'
import { IconButton } from '../components/ui/IconButton'
import { Input, Textarea } from '../components/ui/Input'
import { ModalHeader } from '../components/ui/ModalHeader'
import { PillSwitch } from '../components/ui/PillSwitch'
import { RunningSpinner } from '../components/ui/RunningSpinner'
import { Select } from '../components/ui/Select'
import { Skeleton } from '../components/ui/Skeleton'
import { requestConfirmDialog } from '../components/ui/ConfirmDialog'
import { SettingsBreadcrumb } from '../components/settings/SettingsBreadcrumb'
import { SettingsGroup, SettingsRow } from '../components/settings/SettingsGroup'
import { SettingsPanelShell } from '../components/settings/SettingsPanelShell'
import { DesktopPluginInlineDiff } from '../components/desktopPlugins/DesktopPluginInlineDiff'
import { DesktopPluginSurface } from '../components/desktopPlugins/DesktopPluginSurface'
import type { PluginEntry } from '../stores/pluginStore'
import { usePluginStore } from '../stores/pluginStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { removeToast, showToast } from '../stores/toastStore'
import { openWorkspaceThread } from '../utils/openWorkspaceThread'
import {
  buildDesktopPluginMainViewKey,
  buildDesktopPluginSettingsKey,
  buildDesktopPluginContributionKey,
  registerDesktopPluginSurface,
  publishDesktopPluginGeneration,
  withdrawDesktopPluginGeneration,
  type ActiveDesktopPluginMainView,
  type ActiveDesktopPluginCommand,
  type ActiveDesktopPluginConversationView,
  type ActiveDesktopPluginMessageAction,
  type ActiveDesktopPluginSettingsPage,
  type ActiveDesktopPluginToolRenderer,
  type DesktopPluginGeneration
} from './desktopPluginRegistry'
import { registerDesktopPluginOpenUrlListener } from './desktopPluginOpenUrl'
import {
  emitDesktopPluginEvent,
  onDesktopPluginEvent,
  provideDesktopPluginService,
  useDesktopPluginService
} from './desktopPluginKernel'

interface DesktopPluginModule {
  activate: DesktopPluginActivate
}

interface DesktopPluginTarget {
  plugin: PluginEntry
  version: string
  revision: string
  rootPath: string
}

interface ActiveGeneration {
  generation: DesktopPluginGeneration
  activation: DesktopPluginActivation
  styles: HTMLLinkElement[]
  scope: DesktopPluginCleanupScope
}

interface DesktopPluginCleanupScope {
  active: boolean
  cleanups: Set<() => void>
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
  }): Promise<{ entryUrl: string; styleUrls: string[] }>
  removeModule(params: { pluginId: string; revision: string }): Promise<unknown>
  importModule(url: string): Promise<unknown>
}

export class DesktopPluginRuntime {
  private readonly active = new Map<string, ActiveGeneration>()
  private readonly desired = new Map<string, DesktopPluginTarget>()
  private readonly tokens = new Map<string, number>()
  private readonly pending = new Map<string, PendingGeneration>()
  private stopped = false

  constructor(private readonly dependencies: DesktopPluginRuntimeDependencies) {}

  reconcile(plugins: readonly PluginEntry[]): void {
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
        rootPath: plugin.rootPath
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
            await this.removeModule(pluginId, target.revision)
          }, () => {})
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
      this.active.set(pluginId, { generation, activation, styles, scope })
      published = true
      activation = null
      styles = []
      routeRegistered = false
    } catch (error) {
      if (this.current(pluginId, token, target)) {
        console.error(`Desktop Plugin '${pluginId}' activation failed:`, error)
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
          await this.removeModule(pluginId, target.revision)
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
    await this.removeModule(pluginId, active.generation.revision)
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

  private async removeModule(pluginId: string, revision: string): Promise<void> {
    await this.dependencies.removeModule({ pluginId, revision }).catch((error: unknown) => {
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
}

let runtime: DesktopPluginRuntime | null = null
let unsubscribeStore: (() => void) | null = null

export function startDesktopPluginRuntime(): () => void {
  if (runtime) return stopDesktopPluginRuntime
  installAuthoringRuntime({
    react: React as Parameters<typeof installAuthoringRuntime>[0]['react'],
    jsxRuntime: JsxRuntime,
    reactDom: { createPortal },
    ui: {
      Button,
      IconButton,
      Input,
      Textarea,
      Select,
      Checkbox,
      Spinner: RunningSpinner,
      Skeleton,
      ActionTooltip,
      Combobox,
      ModalHeader,
      PillSwitch,
      SettingsPanelShell,
      SettingsBreadcrumb,
      SettingsGroup,
      SettingsRow,
      InlineDiff: DesktopPluginInlineDiff,
      PluginSurface: DesktopPluginSurface
    }
  })
  runtime = new DesktopPluginRuntime({
    registerModule: (params) => window.api.desktopPlugins.registerModule(params),
    removeModule: (params) => window.api.desktopPlugins.removeModule(params),
    importModule: (url) => import(/* @vite-ignore */ url)
  })
  runtime.reconcile(usePluginStore.getState().plugins)
  unsubscribeStore = usePluginStore.subscribe((state) => runtime?.reconcile(state.plugins))
  return stopDesktopPluginRuntime
}

export function stopDesktopPluginRuntime(): void {
  unsubscribeStore?.()
  unsubscribeStore = null
  const activeRuntime = runtime
  runtime = null
  if (activeRuntime) void activeRuntime.stop()
}

function createDesktopPluginHost(
  plugin: PluginEntry,
  pluginId: string,
  revision: string,
  scope: DesktopPluginCleanupScope
): DesktopPluginHost {
  const cleanups = scope.cleanups
  const own = <T extends () => void>(collection: Set<T>, cleanup: T): T => {
    if (!scope.active) {
      cleanup()
      return (() => {}) as T
    }
    let owned!: T
    owned = (() => {
      if (!collection.delete(owned)) return
      cleanup()
    }) as T
    collection.add(owned)
    return owned
  }
  const host: DesktopPluginHost = {
    plugin: {
      id: plugin.id,
      version: plugin.version!,
      displayName: plugin.displayName
    },
    environment: {
      get locale() {
        return document.documentElement.lang || navigator.language
      },
      get theme() {
        return document.documentElement.dataset.theme === 'dark' ? 'dark' : 'light'
      }
    },
    navigation: {
      openMainView(id) {
        useUIStore.getState().setActiveMainView(buildDesktopPluginMainViewKey(pluginId, id))
      },
      openSettingsPage(id) {
        const ui = useUIStore.getState()
        ui.setActiveSettingsTab(buildDesktopPluginSettingsKey(pluginId, id))
        ui.setActiveMainView('settings')
      },
      async openThread(threadId, workspacePath) {
        const foregroundWorkspacePath = useWorkspaceProjectsStore.getState().foregroundWorkspacePath
        await openWorkspaceThread({
          threadId,
          workspacePath,
          foregroundWorkspacePath,
          switchWorkspace: (nextPath) => window.api.workspace.switch(nextPath),
          setPending: (payload) => useUIStore.getState().setPendingProjectThreadOpen(payload),
          clearPending: (projectKey, pendingThreadId) =>
            useUIStore.getState().clearPendingProjectThreadOpen(projectKey, pendingThreadId),
          activateThread: (targetThreadId) => {
            useThreadStore.getState().setActiveThreadId(targetThreadId)
            useUIStore.getState().setActiveMainView('conversation')
          }
        })
      },
      onOpenUrl(listener) {
        return own(
          cleanups,
          registerDesktopPluginOpenUrlListener(pluginId, revision, listener)
        )
      }
    },
    ui: {
      showToast(options) {
        const toastId = showToast({
          message: options.message,
          type: options.tone === 'neutral' || options.tone == null ? 'info' : options.tone,
          durationMs: options.durationMs,
          action: options.action
            ? { label: options.action.label, onClick: options.action.run }
            : undefined
        })
        const dismiss = () => removeToast(toastId)
        return own(cleanups, dismiss)
      },
      confirm(options) {
        const request = requestConfirmDialog(options)
        const dismiss = own(cleanups, request.dismiss)
        return request.result.finally(dismiss)
      },
      add(surface, component) {
        return own(cleanups, registerDesktopPluginSurface(
          pluginId,
          host,
          surface,
          'add',
          component
        ))
      },
      replace(surface, component) {
        return own(cleanups, registerDesktopPluginSurface(
          pluginId,
          host,
          surface,
          'replace',
          component
        ))
      },
      wrap(surface, component) {
        return own(cleanups, registerDesktopPluginSurface(
          pluginId,
          host,
          surface,
          'wrap',
          component
        ))
      }
    },
    effect(setup) {
      if (!scope.active) return () => {}
      const cleanup = setup()
      return cleanup ? own(cleanups, cleanup) : () => {}
    },
    services: {
      provide(id, service) {
        return own(cleanups, provideDesktopPluginService(id, service))
      },
      use(id) {
        return useDesktopPluginService(id)
      }
    },
    events: {
      on(event, listener) {
        return own(cleanups, onDesktopPluginEvent(event, listener))
      },
      emit(event, payload) {
        emitDesktopPluginEvent(event, payload)
      }
    },
    appServer: {
      request<M extends keyof ClientRequestMethods>(
        method: M,
        params: ClientRequestMethods[M]['params'],
        timeoutMs?: number
      ): Promise<ClientRequestMethods[M]['result']> {
        return window.api.appServer.sendRequestRaw(method, params, timeoutMs) as
          Promise<ClientRequestMethods[M]['result']>
      },
      onNotification<M extends keyof ServerNotificationMethods>(
        method: M,
        listener: (params: ServerNotificationMethods[M]['params']) => void
      ) {
        return own(cleanups, window.api.appServer.onNotificationRaw((notification) => {
          if (notification.method === method) {
            listener(notification.params as ServerNotificationMethods[M]['params'])
          }
        }))
      }
    },
    appBindings: {
      getConnectionStatus(appId) {
        return window.api.desktopPlugins.getAppConnectionStatus({ appId }) as Promise<AppConnectionStatusResult>
      },
      startConnection(appId) {
        return window.api.desktopPlugins.startAppConnection({ appId }) as Promise<AppConnectionStartResult>
      },
      openNativeApp(appId, url) {
        return window.api.desktopPlugins.openApp({ appId, url })
      }
    },
    appSurfaces: {
      getJson<T = unknown>(appId: string, surfaceId: string, relativePath: string, timeoutMs?: number): Promise<T> {
        return window.api.desktopPlugins.appSurfaceGetJson({ appId, surfaceId, relativePath, timeoutMs }) as Promise<T>
      },
      postJson<T = unknown>(
        appId: string,
        surfaceId: string,
        relativePath: string,
        body: unknown,
        timeoutMs?: number
      ): Promise<T> {
        return window.api.desktopPlugins.appSurfacePostJson({
          appId,
          surfaceId,
          relativePath,
          body,
          timeoutMs
        }) as Promise<T>
      }
    },
    workspaces: {
      async listLocalProjects() {
        return useWorkspaceProjectsStore.getState().projects
          .filter((project) => project.kind !== 'remote')
          .map((project) => ({
            path: project.path,
            name: project.name,
            active: project.state === 'foreground'
          }))
      }
    },
    oratorio: {
      getContext: () => window.api.oratorio.getContext(),
      request: (request) => window.api.oratorio.request(request),
      retry: () => window.api.oratorio.retry(),
      getPendingHandoff: () => window.api.oratorio.getPendingHandoff(),
      resolveHandoff: (requestId, approved) => window.api.oratorio.resolveHandoff(requestId, approved),
      focusRun: (runId) => window.api.oratorio.focusRun(runId),
      onEvent: (callback) => own(cleanups, window.api.oratorio.onEvent(callback))
    }
  }
  return host
}

function requireDesktopPluginModule(value: unknown): DesktopPluginModule {
  if (!isRecord(value) || typeof value.activate !== 'function') {
    throw new Error('Desktop Plugin entry must export activate(host).')
  }
  return value as unknown as DesktopPluginModule
}

function validateActivation(
  pluginId: string,
  version: string,
  revision: string,
  host: DesktopPluginHost,
  value: unknown
): DesktopPluginGeneration {
  if (!isRecord(value)) throw new Error('Desktop Plugin activate(host) must return an activation object.')
  const allowedKeys = new Set([
    'mainViews',
    'settingsPages',
    'conversationViews',
    'commands',
    'toolRenderers',
    'messageActions',
    'dispose'
  ])
  const unknownKind = Object.keys(value).find((key) => !allowedKeys.has(key))
  if (unknownKind) throw new Error(`Desktop Plugin activation contains unknown contribution '${unknownKind}'.`)
  if (value.dispose != null && typeof value.dispose !== 'function') {
    throw new Error('Desktop Plugin activation dispose must be a function.')
  }
  const ids = new Set<string>()
  const active = { pluginId, revision, host }
  const mainViews = validateViewContributions<DesktopPluginMainViewContribution>(value.mainViews, 'main view', ids)
    .map<ActiveDesktopPluginMainView>((contribution) => Object.freeze({
      ...contribution,
      ...active,
      viewKey: buildDesktopPluginMainViewKey(pluginId, contribution.id),
    }))
  const settingsPages = validateViewContributions<DesktopPluginSettingsPageContribution>(
    value.settingsPages,
    'settings page',
    ids
  ).map<ActiveDesktopPluginSettingsPage>((contribution) => Object.freeze({
    ...contribution,
    ...active,
    settingsKey: buildDesktopPluginSettingsKey(pluginId, contribution.id),
  }))
  const conversationViews = validateViewContributions<DesktopPluginConversationViewContribution>(
    value.conversationViews,
    'conversation view',
    ids
  ).map<ActiveDesktopPluginConversationView>((contribution) => Object.freeze({
    ...contribution,
    ...active,
    contributionKey: buildDesktopPluginContributionKey(pluginId, contribution.id)
  }))
  const commands = validateCallbackContributions<DesktopPluginCommandContribution>(
    value.commands,
    'command',
    ids
  ).map<ActiveDesktopPluginCommand>((contribution) => Object.freeze({
    ...contribution,
    ...active,
    contributionKey: buildDesktopPluginContributionKey(pluginId, contribution.id)
  }))
  const toolRenderers = validateToolRenderers(value.toolRenderers, ids)
    .map<ActiveDesktopPluginToolRenderer>((contribution) => Object.freeze({
      ...contribution,
      ...active,
      contributionKey: buildDesktopPluginContributionKey(pluginId, contribution.id)
    }))
  const messageActions = validateCallbackContributions<DesktopPluginMessageActionContribution>(
    value.messageActions,
    'message action',
    ids
  ).map<ActiveDesktopPluginMessageAction>((contribution) => Object.freeze({
    ...contribution,
    ...active,
    contributionKey: buildDesktopPluginContributionKey(pluginId, contribution.id)
  }))
  return Object.freeze({
    pluginId,
    version,
    revision,
    mainViews: Object.freeze(mainViews),
    settingsPages: Object.freeze(settingsPages),
    conversationViews: Object.freeze(conversationViews),
    commands: Object.freeze(commands),
    toolRenderers: Object.freeze(toolRenderers),
    messageActions: Object.freeze(messageActions)
  })
}

function validateViewContributions<T extends {
  id: string
  label: { default: string; translations?: Readonly<Record<string, string>> }
  component: unknown
  order?: number
}>(
  value: unknown,
  kind: string,
  ids: Set<string>
): T[] {
  return contributionArray(value, kind).map((candidate) => {
    validateLabeledContribution(candidate, kind, ids)
    if (typeof candidate.component !== 'function') {
      throw new Error(`Desktop Plugin ${kind} '${candidate.id}' requires a component.`)
    }
    return candidate as unknown as T
  })
}

function validateCallbackContributions<T extends {
  id: string
  label: { default: string; translations?: Readonly<Record<string, string>> }
  description?: unknown
  execute: unknown
  isAvailable?: unknown
}>(value: unknown, kind: string, ids: Set<string>): T[] {
  return contributionArray(value, kind).map((candidate) => {
    validateLabeledContribution(candidate, kind, ids)
    if (candidate.description != null) validateLocalizedText(candidate.description, `${kind} '${candidate.id}' description`)
    if (candidate.isAvailable != null && typeof candidate.isAvailable !== 'function') {
      throw new Error(`Desktop Plugin ${kind} '${candidate.id}' has an invalid availability predicate.`)
    }
    if (typeof candidate.execute !== 'function') {
      throw new Error(`Desktop Plugin ${kind} '${candidate.id}' requires execute.`)
    }
    return candidate as unknown as T
  })
}

function validateToolRenderers(value: unknown, ids: Set<string>): DesktopPluginToolRendererContribution[] {
  return contributionArray(value, 'tool renderer').map((candidate) => {
    validateContributionId(candidate, 'tool renderer', ids)
    if (typeof candidate.presentationId !== 'string' || !candidate.presentationId.trim()
      || candidate.presentationId !== candidate.presentationId.trim()) {
      throw new Error(`Desktop Plugin tool renderer '${candidate.id}' requires a presentationId.`)
    }
    if (candidate.priority != null && (typeof candidate.priority !== 'number' || !Number.isFinite(candidate.priority))) {
      throw new Error(`Desktop Plugin tool renderer '${candidate.id}' has an invalid priority.`)
    }
    if (typeof candidate.component !== 'function') {
      throw new Error(`Desktop Plugin tool renderer '${candidate.id}' requires a component.`)
    }
    return candidate as unknown as DesktopPluginToolRendererContribution
  })
}

function contributionArray(value: unknown, kind: string): Record<string, unknown>[] {
  if (value == null) return []
  if (!Array.isArray(value)) throw new Error(`Desktop Plugin ${kind} contributions must be an array.`)
  return value.map((candidate) => {
    if (!isRecord(candidate)) throw new Error(`Desktop Plugin ${kind} contribution must be an object.`)
    return candidate
  })
}

function validateLabeledContribution(candidate: Record<string, unknown>, kind: string, ids: Set<string>): void {
  validateContributionId(candidate, kind, ids)
  validateLocalizedText(candidate.label, `${kind} '${candidate.id}' label`)
  if (candidate.order != null && (typeof candidate.order !== 'number' || !Number.isFinite(candidate.order))) {
    throw new Error(`Desktop Plugin ${kind} '${candidate.id}' has an invalid order.`)
  }
}

function validateContributionId(candidate: Record<string, unknown>, kind: string, ids: Set<string>): void {
  if (typeof candidate.id !== 'string' || !candidate.id.trim()) {
    throw new Error(`Desktop Plugin ${kind} id is required.`)
  }
  if (candidate.id !== candidate.id.trim() || ids.has(candidate.id)) {
    throw new Error(`Desktop Plugin ${kind} id '${candidate.id}' is duplicated or invalid.`)
  }
  ids.add(candidate.id)
}

function validateLocalizedText(value: unknown, field: string): void {
  if (!isRecord(value) || typeof value.default !== 'string' || !value.default.trim()) {
    throw new Error(`Desktop Plugin ${field} is required.`)
  }
}

function installStyles(pluginId: string, revision: string, urls: readonly string[]): HTMLLinkElement[] {
  const links = urls.map((url, index) => {
    const link = document.createElement('link')
    link.rel = 'stylesheet'
    link.href = url
    link.dataset.dotcraftDesktopPlugin = pluginId
    link.dataset.dotcraftDesktopPluginRevision = revision
    link.dataset.dotcraftDesktopPluginStyle = String(index)
    return link
  })
  for (const link of links) insertStyleInOrder(link)
  return links
}

function insertStyleInOrder(link: HTMLLinkElement): void {
  const key = styleKey(link)
  const next = [...document.head.querySelectorAll<HTMLLinkElement>('link[data-dotcraft-desktop-plugin]')]
    .find((candidate) => styleKey(candidate) > key)
  document.head.insertBefore(link, next ?? null)
}

function styleKey(link: HTMLLinkElement): string {
  return `${link.dataset.dotcraftDesktopPlugin ?? ''}\0${(link.dataset.dotcraftDesktopPluginStyle ?? '').padStart(8, '0')}`
}

function removeStyles(links: readonly HTMLLinkElement[]): void {
  for (const link of links) link.remove()
}

function disposeDesktopPluginCleanupScope(scope: DesktopPluginCleanupScope): void {
  if (!scope.active) return
  scope.active = false
  for (const cleanup of [...scope.cleanups]) void callCleanup(cleanup)
}

async function callCleanup(cleanup: () => void | Promise<void>): Promise<void> {
  try {
    await cleanup()
  } catch (error) {
    console.error('Desktop Plugin cleanup failed:', error)
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}
