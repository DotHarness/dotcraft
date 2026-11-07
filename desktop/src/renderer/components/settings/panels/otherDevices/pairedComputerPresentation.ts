import type { MessageKey } from '../../../../../shared/locales'
import { lastSeenLabel } from '../../../../../shared/satellites'
import type { PairedComputer, PairedComputerStatus } from '../../../../../shared/pairedComputers'
import type { StatusIndicatorTone } from '../../../ui/StatusIndicator'

type Translate = (key: MessageKey, vars?: Record<string, string | number>) => string

export const STATUS_LABEL: Record<PairedComputerStatus, MessageKey> = {
  connecting: 'settings.otherDevices.status.connecting',
  connected: 'settings.otherDevices.status.connected',
  offline: 'settings.otherDevices.status.offline',
  accessOff: 'settings.otherDevices.status.accessOff',
  updateRequired: 'settings.otherDevices.status.updateRequired',
  identityChanged: 'settings.otherDevices.status.identityChanged'
}

export const STATUS_TONE: Record<PairedComputerStatus, StatusIndicatorTone> = {
  connecting: 'pending',
  connected: 'success',
  offline: 'neutral',
  accessOff: 'neutral',
  updateRequired: 'warning',
  identityChanged: 'error'
}

export function statusMessage(t: Translate, computer: PairedComputer): string | null {
  switch (computer.status) {
    case 'connected':
      return computer.version ? t('settings.otherDevices.detail.version', { version: computer.version }) : null
    case 'offline': {
      if (!computer.lastConnectedAt) return null
      const seen = lastSeenLabel(computer.lastConnectedAt)
      return t('settings.otherDevices.message.lastConnected', { time: t(seen.key as MessageKey, seen.params) })
    }
    case 'accessOff':
      return t('settings.otherDevices.message.accessOff')
    case 'updateRequired':
      return t('settings.otherDevices.message.updateRequired', {
        current: computer.version ?? '?',
        required: computer.requiredVersion ?? '?'
      })
    case 'identityChanged':
      return t('settings.otherDevices.message.identityChanged')
    case 'connecting':
      return null
  }
}

export function projectsBlockedReason(t: Translate, computer: PairedComputer): string | undefined {
  if (computer.status === 'connected') return undefined
  return t(
    computer.status === 'updateRequired'
      ? 'settings.otherDevices.projects.blockedByUpdate'
      : 'settings.otherDevices.projects.blockedByConnection'
  )
}
