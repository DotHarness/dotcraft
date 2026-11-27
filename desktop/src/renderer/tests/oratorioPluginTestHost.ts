import type { DesktopPluginHost } from '@dotcraft/plugin'
import { removeToast, showToast } from '../stores/toastStore'
import { installOratorioHost } from '../../bundled-plugins/oratorio/src/runtime'

export function installOratorioTestHost(overrides: Partial<DesktopPluginHost> = {}): DesktopPluginHost {
  const api = window.api.oratorio
  const host: DesktopPluginHost = {
    plugin: { id: 'oratorio', version: '0.5.9', displayName: 'Oratorio' },
    environment: {
      get locale() { return document.documentElement.lang || 'en' },
      theme: 'light',
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
      showToast(options) {
        const id = showToast({
          message: options.message,
          type: options.tone === 'neutral' || options.tone == null ? 'info' : options.tone,
          action: options.action ? { label: options.action.label, onClick: options.action.run } : undefined
        })
        return () => removeToast(id)
      },
      requestSpaceGuide() {},
      async confirm() { return true },
      add() { return () => undefined },
      wrap() { return () => undefined }
    } as DesktopPluginHost['ui'],
    session: { workspacePath: null, threadId: null, mode: 'agent', busy: false, onChange() { return () => undefined } },
    threads: { findOwned: () => null } as unknown as DesktopPluginHost['threads'],
    appServer: { request: async () => { throw new Error('Unexpected AppServer request.') }, onNotification: () => () => undefined },
    workspaces: {
      async listLocalProjects() {
        const payload = await window.api.workspace.getProjects()
        return payload.projects
          .filter((project) => project.kind === 'local')
          .map((project) => ({ path: project.path, name: project.name, lastOpenedAt: project.lastOpenedAt, active: project.state === 'foreground' }))
      }
    },
    oratorio: {
      getContext: () => api.getContext(),
      request: (request) => api.request(request),
      retry: () => api.retry(),
      focusRun: (runId) => api.focusRun(runId),
      onEvent: (listener) => api.onEvent(listener)
    },
    ...overrides
  }
  installOratorioHost(host)
  return host
}
