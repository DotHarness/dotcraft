import { useSyncExternalStore } from 'react'
import type { DesktopPluginHost, DesktopPluginRemoteToolHostSnapshot } from '@dotcraft/plugin'
import { translate } from './i18n'

let activeHost: DesktopPluginHost | null = null

export function installSatelliteHost(host: DesktopPluginHost): () => void {
  activeHost = host
  return () => { if (activeHost === host) activeHost = null }
}

export function satelliteHost(): DesktopPluginHost {
  if (!activeHost) throw new Error('Satellite Desktop extension is not active.')
  return activeHost
}

export function useRemoteToolHost(): DesktopPluginRemoteToolHostSnapshot {
  const api = satelliteHost().remoteToolHost
  return useSyncExternalStore(api.onChange, api.getSnapshot, api.getSnapshot)
}

export function showRouteError(hostName: string, error: unknown): void {
  const host = satelliteHost()
  const locale = host.environment.locale
  const raw = error as { message?: string; data?: { messageKey?: string; params?: Record<string, string | number>; fallbackText?: string } } | null
  const key = raw?.data?.messageKey
  const localized = key ? translate(locale, key, raw?.data?.params) : undefined
  host.ui.showToast({
    tone: 'error',
    message: translate(locale, 'error.remoteToolHost.title', { name: hostName }),
    description: localized && localized !== key ? localized : raw?.data?.fallbackText ?? raw?.message ?? String(error)
  })
}
