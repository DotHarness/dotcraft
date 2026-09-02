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
      async confirm() { return true }
    },
    appServer: { request: async () => { throw new Error('Unexpected AppServer request.') }, onNotification: () => () => undefined },
    appBindings: {
      getConnectionStatus: async () => { throw new Error('Unexpected app binding request.') },
      startConnection: async () => { throw new Error('Unexpected app binding request.') },
      openNativeApp: async () => undefined
    },
    appSurfaces: {
      getJson: async () => { throw new Error('Unexpected app surface request.') },
      postJson: async () => { throw new Error('Unexpected app surface request.') }
    },
    workspaces: {
      async listLocalProjects() {
        const payload = await window.api.workspace.getProjects()
        return payload.projects
          .filter((project) => project.kind === 'local')
          .map((project) => ({ path: project.path, name: project.name, active: project.state === 'foreground' }))
      }
    },
    oratorio: {
      getContext: () => api.getContext(),
      request: (request) => api.request(request),
      retry: () => api.retry(),
      getPendingHandoff: () => api.getPendingHandoff(),
      resolveHandoff: (requestId, approved) => api.resolveHandoff(requestId, approved),
      focusRun: (runId) => api.focusRun(runId),
      onEvent: (listener) => api.onEvent(listener)
    },
    ...overrides
  }
  installOratorioHost(host)
  return host
}
