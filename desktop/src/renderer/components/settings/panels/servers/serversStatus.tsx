import type { JSX, ReactNode } from 'react'

import { useRemoteServersStore } from '../../../../stores/remoteServersStore'
import type { RemoteStackStatus, StackHealth } from '../../../../../shared/remoteServers'
import { StatusIndicator } from '../settingsStatusStyles'
import * as s from './serversStyles'

export type TFunction = (key: string, vars?: Record<string, string | number>) => string

export function healthTone(health: StackHealth | undefined): s.StatusTone {
  switch (health) {
    case 'running':
      return 'success'
    case 'partial':
      return 'warning'
    case 'unhealthy':
      return 'error'
    default:
      return 'neutral'
  }
}

export function healthLabel(status: RemoteStackStatus | undefined, t: TFunction): string {
  if (!status) return t('settings.servers.status.notChecked')
  switch (status.health) {
    case 'running':
      return t('settings.servers.status.running')
    case 'partial':
      return t('settings.servers.status.partial', {
        up: status.servicesUp,
        total: status.servicesTotal
      })
    case 'unhealthy':
      return t('settings.servers.status.unhealthy')
    case 'stopped':
      return t('settings.servers.status.stopped')
    default:
      return status.error ? t('settings.servers.status.unavailable') : t('settings.servers.status.unknown')
  }
}

export function reachabilityView(
  hostId: string,
  t: TFunction
): { tone: s.StatusIndicatorTone; label: string } {
  const result = useRemoteServersStore.getState().testResults[hostId]
  if (useRemoteServersStore.getState().testing[hostId]) {
    return { tone: 'pending', label: t('settings.servers.reach.checking') }
  }
  if (!result) return { tone: 'neutral', label: t('settings.servers.reach.notChecked') }
  if (result.reachable) return { tone: 'success', label: t('settings.servers.reach.online') }
  return { tone: 'error', label: t('settings.servers.reach.offline') }
}

export function StatusText({
  tone,
  children
}: {
  tone: s.StatusIndicatorTone
  children: ReactNode
}): JSX.Element {
  return (
    <span style={s.statusTextStyle()}>
      <StatusIndicator tone={tone} />
      {children}
    </span>
  )
}
