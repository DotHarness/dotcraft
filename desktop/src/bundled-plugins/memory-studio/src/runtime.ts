import type { DesktopPluginHost } from '@dotcraft/plugin'

let activeHost: DesktopPluginHost | null = null

export function installMemoryStudioHost(host: DesktopPluginHost): () => void {
  activeHost = host
  return () => { if (activeHost === host) activeHost = null }
}

export function memoryStudioHost(): DesktopPluginHost {
  if (!activeHost) throw new Error('Memory Studio Desktop extension is not active.')
  return activeHost
}
