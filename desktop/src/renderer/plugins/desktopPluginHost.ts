import { useDetailPanelStore } from '../stores/detailPanelStore'
import { settingsTabForSection } from '../components/settings/coreSettingsSections'
import { createDesktopPluginSatellites, createDesktopPluginRemoteToolHost } from './desktopPluginSatellites'
import { createDesktopPluginScreenView } from './desktopPluginScreenView'
import type {
  DesktopPluginHost,
  DesktopPluginSettingsMutation,
  DesktopPluginSettingsScope,
  DesktopPluginSettingsSnapshot
} from '@dotcraft/plugin'
import type {
  ClientRequestMethods,
  ServerNotificationMethods
} from '@dotcraft/sdk/contracts'

import { requestColorPickerDialog } from '../components/ui/ColorPickerDialog'
import { loadLocalImageDataUrl } from '../components/conversation/imagePreview/galleryImages'
import { stageChatDraft, stageSkillRequestInChat } from '../components/conversation/skillRequestDraft'
import { requestConfirmDialog } from '../components/ui/ConfirmDialog'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import { useConnectionStore } from '../stores/connectionStore'
import type { PluginEntry } from '../stores/pluginStore'
import { useThreadStore } from '../stores/threadStore'
import { removeToast, showToast } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { useFeatureGuideStore } from '../stores/featureGuideStore'
import { openActiveThreadFile, openAutomation, openExternalUrl, openThread, revealToolCall } from '../utils/desktopNavigation'
import { openSkill } from '../utils/openSkill'
import { openSubAgent } from '../utils/subAgentNavigation'
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
import { callDesktopPluginMcpTool } from './desktopPluginMcp'
import { listDesktopPluginOpenTargets, openDesktopPluginPath } from './desktopPluginOpenTargets'
import { registerDesktopPluginOpenUrlListener } from './desktopPluginOpenUrl'
import {
  listDesktopPluginProjectActions,
  onDesktopPluginProjectActionsChange,
  openDesktopPluginProjectSettings,
  openDesktopPluginProjectTerminal,
  promptDesktopPluginProjectAction,
  runDesktopPluginProjectAction,
  selectDesktopPluginProjectEnvironment
} from './desktopPluginProjectActions'
import {
  buildDesktopPluginContributionKey,
  buildDesktopPluginMainViewKey,
  buildDesktopPluginSettingsKey,
  registerDesktopPluginSurface
} from './desktopPluginRegistry'
import {
  onDesktopPluginSessionChange,
  readDesktopPluginSession
} from './desktopPluginSession'
import { createDesktopPluginThreads } from './desktopPluginThreads'
import {
  listDesktopPluginSubAgents,
  onDesktopPluginSubAgentsChange
} from './desktopPluginSubAgents'
import {
  mutateDesktopPluginSettings,
  onDesktopPluginSettingsChange,
  readDesktopPluginSettings
} from './desktopPluginSettings'

export interface DesktopPluginCleanupScope {
  active: boolean
  cleanups: Set<() => void>
}

function foregroundWorkspaceKey(): string {
  const { foregroundProjectId, foregroundWorkspacePath } = useWorkspaceProjectsStore.getState()
  return normalizeWorkspaceProjectKey(foregroundProjectId || foregroundWorkspacePath)
}

function connectionMatchesWorkspace(
  conn: ReturnType<typeof useConnectionStore.getState>,
  targetKey: string
): boolean {
  return !conn.connectionKey || normalizeWorkspaceProjectKey(conn.connectionKey) === targetKey
}

function waitForForegroundAppServerConnection(
  scope: DesktopPluginCleanupScope,
  own: <T extends () => void>(collection: Set<T>, cleanup: T) => T,
  targetKey: string,
  timeoutMs?: number
): Promise<string | undefined> {
  const current = useConnectionStore.getState()
  if (current.connectionEpoch === 0 || !targetKey) return Promise.resolve(current.connectionId ?? undefined)
  if (current.status === 'connected' && connectionMatchesWorkspace(current, targetKey)) {
    return Promise.resolve(current.connectionId ?? undefined)
  }
  if (current.status !== 'connected' && current.status !== 'connecting') {
    return Promise.resolve(undefined)
  }
  if (!scope.active) return Promise.reject(new Error('Plugin deactivated before the workspace connected.'))

  return new Promise<string | undefined>((resolve, reject) => {
    let settled = false
    const finish = (outcome: () => void): void => {
      if (settled) return
      settled = true
      if (timer != null) clearTimeout(timer)
      stopConnection()
      stopProjects()
      scope.cleanups.delete(disposeEntry)
      outcome()
    }
    const evaluate = (): void => {
      if (foregroundWorkspaceKey() !== targetKey) {
        finish(() => reject(new Error('Workspace changed before the request could be sent.')))
        return
      }
      const conn = useConnectionStore.getState()
      if (conn.status === 'connected') {
        if (connectionMatchesWorkspace(conn, targetKey)) finish(() => resolve(conn.connectionId ?? undefined))
      } else if (conn.status !== 'connecting') {
        finish(() => resolve(undefined))
      }
    }
    const stopConnection = useConnectionStore.subscribe(evaluate)
    const stopProjects = useWorkspaceProjectsStore.subscribe(evaluate)
    const disposeEntry = own(scope.cleanups, () => {
      finish(() => reject(new Error('Plugin deactivated before the workspace connected.')))
    })
    const timer = timeoutMs != null && timeoutMs > 0
      ? setTimeout(() => finish(() => reject(new Error('Timed out waiting for workspace connection.'))), timeoutMs)
      : null
  })
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
    subagents: {
      list(parentThreadId) {
        return listDesktopPluginSubAgents(parentThreadId)
      },
      onChange(parentThreadId, listener) {
        return own(cleanups, onDesktopPluginSubAgentsChange(parentThreadId, listener))
      },
      reveal(parentThreadId, childThreadId) {
        const show = (): void => {
          useUIStore.getState().setActiveMainView('conversation')
          openSubAgent(parentThreadId, childThreadId)
        }
        if (useThreadStore.getState().activeThreadId === parentThreadId) show()
        else void host.navigation.openThread(parentThreadId).then(show)
      }
    },
    threads: createDesktopPluginThreads(pluginId, (cleanup) => own(cleanups, cleanup)),
    projectActions: {
      list(workspacePath) {
        return listDesktopPluginProjectActions(workspacePath)
      },
      onChange(workspacePath, listener) {
        return own(cleanups, onDesktopPluginProjectActionsChange(workspacePath, listener))
      },
      select(workspacePath, environmentId) {
        return selectDesktopPluginProjectEnvironment(workspacePath, environmentId)
      },
      run(request) {
        return runDesktopPluginProjectAction(request)
      },
      openTerminal(request) {
        return openDesktopPluginProjectTerminal(request)
      },
      promptAddAction(request) {
        return promptDesktopPluginProjectAction(request, (dismiss) => own(cleanups, dismiss))
      },
      openSettings(request) {
        return openDesktopPluginProjectSettings({ workspacePath: request.workspacePath, environmentId: request.environmentId })
      }
    },
    navigation: {
      openMainView(id) {
        useUIStore.getState().setActiveMainView(buildDesktopPluginMainViewKey(pluginId, id))
      },
      openSettingsPage(id) {
        useUIStore.getState().openSettings(buildDesktopPluginSettingsKey(pluginId, id))
      },
      openSettingsSection(section) {
        const tab = settingsTabForSection(section)
        if (!tab) return
        useUIStore.getState().openSettings(tab)
      },
      openThread(threadId, workspacePath) {
        return openThread(threadId, workspacePath)
      },
      startChat({ prompt, skill }) {
        if (skill) return stageSkillRequestInChat(skill, prompt)
        stageChatDraft(prompt, [{ type: 'text', value: prompt }])
        return Promise.resolve()
      },
      openExternal(url) {
        return openExternalUrl(url)
      },
      openFile(path) {
        return openActiveThreadFile(path)
      },
      openPath(path, options) {
        return openDesktopPluginPath(path, options)
      },
      listOpenTargets() {
        return listDesktopPluginOpenTargets()
      },
      openDetailPanel(tab) {
        useDetailPanelStore.getState().setActiveDetailTab(tab)
      },
      openAutomation(automationId) {
        openAutomation(automationId)
      },
      openSkill(name) {
        openSkill(name)
      },
      revealToolCall(threadId, callId) {
        revealToolCall(threadId, callId)
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
          description: options.description,
          icon: options.icon,
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
      pickFolder(options) {
        return window.api.workspace.pickFolder({ title: options.title })
      },
      requestSpaceGuide(spaceId) {
        if (!scope.active) return
        useFeatureGuideStore.getState().requestSpaceGuide(buildDesktopPluginContributionKey(pluginId, spaceId), host)
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
      async request<M extends keyof ClientRequestMethods>(
        method: M,
        params: ClientRequestMethods[M]['params'],
        timeoutMs?: number
      ): Promise<ClientRequestMethods[M]['result']> {
        const connectionId = await waitForForegroundAppServerConnection(scope, own, foregroundWorkspaceKey(), timeoutMs)
        const response = connectionId
          ? window.api.appServer.sendRequestRaw(method, params, timeoutMs, { connectionId })
          : window.api.appServer.sendRequestRaw(method, params, timeoutMs)
        return response as Promise<ClientRequestMethods[M]['result']>
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
    mcp: {
      callTool: callDesktopPluginMcpTool
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
    workspaces: {
      async listLocalProjects() {
        return useWorkspaceProjectsStore.getState().projects
          .filter((project) => project.kind !== 'remote')
          .map((project) => ({
            path: project.path,
            name: project.name,
            lastOpenedAt: project.lastOpenedAt,
            active: project.state === 'foreground'
          }))
      },
      async readImageAttachment(path) {
        return loadLocalImageDataUrl(path, useThreadStore.getState().activeThreadId ?? undefined)
      }
    },
    satellites: createDesktopPluginSatellites(scope),
    remoteToolHost: createDesktopPluginRemoteToolHost(scope),
    screenView: createDesktopPluginScreenView(scope),
    oratorio: {
      getContext: () => window.api.oratorio.getContext(),
      request: (request) => window.api.oratorio.request(request),
      retry: () => window.api.oratorio.retry(),
      focusRun: (runId) => window.api.oratorio.focusRun(runId),
      onEvent: (callback) => own(cleanups, window.api.oratorio.onEvent(callback))
    }
  }
  own(cleanups, () => useFeatureGuideStore.getState().clearSpaceRequests(host))
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
    console.error('Desktop extension cleanup failed:', error)
  }
}
