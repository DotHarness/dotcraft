import type {
  DesktopPluginHost,
  DesktopPluginSettingsMutation,
  DesktopPluginSettingsScope,
  DesktopPluginSettingsSnapshot
} from '@dotcraft/plugin'
import type {
  AppConnectionStartResult,
  AppConnectionStatusResult,
  ClientRequestMethods,
  ServerNotificationMethods
} from '@dotcraft/sdk/contracts'

import { requestColorPickerDialog } from '../components/ui/ColorPickerDialog'
import { requestConfirmDialog } from '../components/ui/ConfirmDialog'
import type { PluginEntry } from '../stores/pluginStore'
import { useThreadStore } from '../stores/threadStore'
import { removeToast, showToast } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { openWorkspaceThread } from '../utils/openWorkspaceThread'
import { registerDesktopPluginAppearanceSlot } from './desktopPluginAppearance'
import {
  onDesktopPluginEnvironmentChange,
  readDesktopPluginEnvironment
} from './desktopPluginEnvironment'
import {
  emitDesktopPluginEvent,
  onDesktopPluginEvent,
  provideDesktopPluginService,
  useDesktopPluginService
} from './desktopPluginKernel'
import { registerDesktopPluginOpenUrlListener } from './desktopPluginOpenUrl'
import {
  buildDesktopPluginMainViewKey,
  buildDesktopPluginSettingsKey,
  registerDesktopPluginSurface
} from './desktopPluginRegistry'
import {
  onDesktopPluginSessionChange,
  readDesktopPluginSession
} from './desktopPluginSession'
import {
  mutateDesktopPluginSettings,
  onDesktopPluginSettingsChange,
  readDesktopPluginSettings
} from './desktopPluginSettings'

export interface DesktopPluginCleanupScope {
  active: boolean
  cleanups: Set<() => void>
}

export function createDesktopPluginHost(
  plugin: PluginEntry,
  pluginId: string,
  revision: string,
  scope: DesktopPluginCleanupScope
): DesktopPluginHost {
  const cleanups = scope.cleanups
  const own = <T extends () => void>(collection: Set<T>, cleanup: T): T => {
    if (!scope.active) {
      cleanup()
      return (() => { }) as T
    }
    let owned!: T
    owned = (() => {
      if (!collection.delete(owned)) return
      cleanup()
    }) as T
    collection.add(owned)
    return owned
  }
  const appearance = registerDesktopPluginAppearanceSlot(`${pluginId}:${revision}`)
  own(cleanups, appearance.dispose)
  const host: DesktopPluginHost = {
    plugin: {
      id: plugin.id,
      version: plugin.version!,
      displayName: plugin.displayName
    },
    environment: {
      get locale() {
        return readDesktopPluginEnvironment().locale
      },
      get theme() {
        return readDesktopPluginEnvironment().theme
      },
      get themeSeed() {
        return readDesktopPluginEnvironment().themeSeed
      },
      onChange(listener) {
        return own(cleanups, onDesktopPluginEnvironmentChange(listener))
      }
    },
    appearance: {
      setThemeSeedOverride(value) {
        appearance.setThemeSeedOverride(value)
      },
      setBackdropPresentation(value) {
        appearance.setBackdropPresentation(value)
      }
    },
    session: {
      get workspacePath() {
        return readDesktopPluginSession().workspacePath
      },
      get threadId() {
        return readDesktopPluginSession().threadId
      },
      get mode() {
        return readDesktopPluginSession().mode
      },
      get busy() {
        return readDesktopPluginSession().busy
      },
      onChange(listener) {
        return own(cleanups, onDesktopPluginSessionChange(listener))
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
      async openExternal(url) {
        await window.api.shell.openExternal(url)
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
      pickColor(options) {
        let request: ReturnType<typeof requestColorPickerDialog>
        try {
          request = requestColorPickerDialog(options)
        } catch (error) {
          return Promise.reject(error)
        }
        const dismiss = own(cleanups, request.dismiss)
        return request.result.finally(dismiss)
      },
      add(surface, component, options) {
        return own(cleanups, registerDesktopPluginSurface(
          pluginId,
          host,
          surface,
          'add',
          component,
          options
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
      if (!scope.active) return () => { }
      const cleanup = setup()
      return cleanup ? own(cleanups, cleanup) : () => { }
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
    settings: {
      get<TValue = Record<string, unknown>>() {
        return readDesktopPluginSettings(pluginId) as Promise<DesktopPluginSettingsSnapshot<TValue>>
      },
      mutate<TValue = Record<string, unknown>>(
        scope: DesktopPluginSettingsScope,
        operations: readonly DesktopPluginSettingsMutation[]
      ) {
        return mutateDesktopPluginSettings(pluginId, scope, operations) as
          Promise<DesktopPluginSettingsSnapshot<TValue>>
      },
      onChange<TValue = Record<string, unknown>>(
        listener: (settings: DesktopPluginSettingsSnapshot<TValue>) => void
      ) {
        return own(cleanups, onDesktopPluginSettingsChange(
          pluginId,
          listener as (settings: DesktopPluginSettingsSnapshot<Record<string, unknown>>) => void
        ))
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

export function installStyles(pluginId: string, revision: string, urls: readonly string[]): HTMLLinkElement[] {
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

export function removeStyles(links: readonly HTMLLinkElement[]): void {
  for (const link of links) link.remove()
}

export function disposeDesktopPluginCleanupScope(scope: DesktopPluginCleanupScope): void {
  if (!scope.active) return
  scope.active = false
  for (const cleanup of [...scope.cleanups]) void callCleanup(cleanup)
}

export async function callCleanup(cleanup: () => void | Promise<void>): Promise<void> {
  try {
    await cleanup()
  } catch (error) {
    console.error('Desktop Plugin cleanup failed:', error)
  }
}
