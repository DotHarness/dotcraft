import type { DesktopPluginHost, DesktopPluginToastOptions } from '@dotcraft/plugin'

let activeHost: DesktopPluginHost | null = null

export function installOratorioHost(host: DesktopPluginHost): () => void {
  activeHost = host
  return () => {
    if (activeHost === host) activeHost = null
  }
}

export function oratorioHost(): DesktopPluginHost {
  if (!activeHost) throw new Error('Oratorio Desktop Plugin is not active.')
  return activeHost
}

export function showOratorioToast(options: DesktopPluginToastOptions): () => void {
  return oratorioHost().ui.showToast(options)
}
