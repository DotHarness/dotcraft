import type { JSX, ReactNode } from 'react'

import { useRemoteServersStore } from '../../../../stores/remoteServersStore'
import type { RemoteStackStatus, StackHealth } from '../../../../../shared/remoteServers'
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

export function reachabilityView(hostId: string, t: TFunction): { tone: s.StatusTone; label: string } {
  const result = useRemoteServersStore.getState().testResults[hostId]
  if (useRemoteServersStore.getState().testing[hostId]) {
    return { tone: 'info', label: t('settings.servers.reach.checking') }
  }
  if (!result) return { tone: 'neutral', label: t('settings.servers.reach.notChecked') }
  if (result.reachable) return { tone: 'success', label: t('settings.servers.reach.online') }
  return { tone: 'error', label: t('settings.servers.reach.offline') }
}

export function StatusDot({ tone }: { tone: s.StatusTone }): JSX.Element {
  return <span style={s.dotStyle(tone)} />
}

/** `labelTone` keeps the text neutral while the dot carries the hue, per DESIGN.md. */
export function StatusText({
  tone,
  labelTone = tone,
  children
}: {
  tone: s.StatusTone
  labelTone?: s.StatusTone
  children: ReactNode
}): JSX.Element {
  return (
    <span style={s.statusTextStyle(labelTone)}>
      <StatusDot tone={tone} />
      {children}
    </span>
  )
}
