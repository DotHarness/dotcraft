import type { DesktopPluginHost, DesktopPluginLocale } from '@dotcraft/plugin'
import { removeToast, showToast } from '../../../renderer/stores/toastStore'
import { installOratorioHost } from './runtime'

export function installOratorioTestHost(overrides: Partial<DesktopPluginHost> = {}): DesktopPluginHost {
  const api = window.api.oratorio
  const host = {
    plugin: { id: 'oratorio', version: '0.15.0', displayName: 'Oratorio' },
    environment: {
      get locale() { return (document.documentElement.lang || 'en') as DesktopPluginLocale },
      theme: 'light' as const,
      onChange() { return () => undefined }
    },
    navigation: {
      openMainView() {},
      openSettingsPage() {},
      openSettingsSection() {},
      async openThread() {},
      async openExternal() {},
      onOpenUrl() { return () => undefined }
    },
    ui: {
      showToast(options: Parameters<DesktopPluginHost['ui']['showToast']>[0]) {
        const id = showToast({
          message: options.message,
          type: options.tone === 'neutral' || options.tone == null ? 'info' : options.tone,
          action: options.action ? { label: options.action.label, onClick: options.action.run } : undefined
        })
        return () => removeToast(id)
      },
      requestSpaceGuide() {},
      async confirm() { return true },
      addThreadMenuItems() { return () => undefined },
      add() { return () => undefined },
      wrap() { return () => undefined }
    },
    effect(setup: () => void | (() => void)) {
      const cleanup = setup()
      return typeof cleanup === 'function' ? cleanup : () => undefined
    },
    session: { workspacePath: null, threadId: null, mode: 'agent' as const, busy: false, onChange() { return () => undefined } },
    threads: {
      findOwned: () => null,
      onOwnedChange: () => () => undefined,
      setOwnedPaused: async () => null,
      archiveOwned: async () => false,
      listRunningThreads: async () => ({ activeThreads: [], recentFinished: [] }),
      startBackgroundCheckIn: async () => ({ started: false }),
      linked: async () => [],
      onLinkedChange: () => () => undefined
    },
    appServer: { request: async () => { throw new Error('Unexpected AppServer request.') }, onNotification: () => () => undefined },
    workspaces: {
      async listLocalProjects() {
        const payload = await window.api.workspace.getProjects()
        return payload.projects
          .filter((project) => project.kind === 'local')
          .map((project) => ({ path: project.path, name: project.name, lastOpenedAt: project.lastOpenedAt, active: project.state === 'foreground' }))
      },
      readImageAttachment: async () => '',
      onProjectConnected: () => () => undefined
    },
    oratorio: {
      getContext: () => api.getContext(),
      request: (request: Parameters<DesktopPluginHost['oratorio']['request']>[0]) => api.request(request),
      retry: () => api.retry(),
      focusRun: (runId: string | null) => api.focusRun(runId),
      onEvent: (listener: Parameters<DesktopPluginHost['oratorio']['onEvent']>[0]) => api.onEvent(listener)
    },
    ...overrides
  } as unknown as DesktopPluginHost
  installOratorioHost(host)
  return host
}
