import type { ScreenViewStatus } from '../../../../shared/screenView'

export type ScreenViewSurfaceKind = 'live' | 'connecting' | 'waiting' | 'idle'

export function screenViewStateLabelKey(status: ScreenViewStatus): string {
  if (status.kind === 'paused') {
    return status.needsAuthorization ? 'screenView.state.needsAuthorization' : 'screenView.state.paused'
  }
  if (status.kind === 'unavailable') return `screenView.state.${status.reason ?? 'unavailable'}`
  return `screenView.state.${status.kind}`
}

export function screenViewSurfaceKind(status: ScreenViewStatus): ScreenViewSurfaceKind {
  if (status.kind === 'live') return 'live'
  if (status.kind === 'connecting') return 'connecting'
  if (status.kind === 'stalled' || status.kind === 'reconnecting') return 'waiting'
  return 'idle'
}

export function screenViewStateTitle(hostName: string, stateLabel: string, status: ScreenViewStatus): string {
  const detail = status.kind === 'unavailable' ? status.detail?.trim() : undefined
  return detail ? `${hostName} · ${stateLabel} · ${detail}` : `${hostName} · ${stateLabel}`
}
