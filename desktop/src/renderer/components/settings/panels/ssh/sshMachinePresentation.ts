import type { StatusIndicatorTone, StatusTone } from '../../../ui/StatusIndicator'
import type { MessageKey } from '../../../../../shared/locales'
import {
  formatResolvedHost,
  machineHasHub,
  type SshMachineStatusKind,
  type SshMachineSystemInfo,
  type SshMachineView
} from '../../../../../shared/sshMachines'

type Translate = (key: MessageKey, vars?: Record<string, string | number>) => string

export type MachineAction = 'install' | 'update' | 'setup' | 'reconnect'

export interface MachineStatusView {
  tone: StatusIndicatorTone
  label: string
  message?: string
  action?: MachineAction
}

const LABEL_KEYS: Record<SshMachineStatusKind, MessageKey> = {
  notConnected: 'settings.ssh.status.notConnected',
  connecting: 'settings.ssh.status.connecting',
  connected: 'settings.ssh.status.connected',
  notInstalled: 'settings.ssh.status.notInstalled',
  installing: 'settings.ssh.status.installing',
  updateRequired: 'settings.ssh.status.updateRequired',
  setupModel: 'settings.ssh.status.setupModel',
  failed: 'settings.ssh.status.failed',
  unsupported: 'settings.ssh.status.unsupported'
}

export const ACTION_KEYS: Record<MachineAction, MessageKey> = {
  install: 'settings.ssh.action.install',
  update: 'settings.ssh.action.update',
  setup: 'settings.ssh.action.setup',
  reconnect: 'settings.ssh.action.reconnect'
}

export function osLabel(system: SshMachineSystemInfo | undefined): string | undefined {
  if (!system) return undefined
  const named = [system.osName, system.osVersion].filter(Boolean).join(' ')
  if (named) return named
  if (system.os === 'Darwin') return 'macOS'
  return system.os || undefined
}

export function systemLabel(system: SshMachineSystemInfo | undefined): string | undefined {
  const os = osLabel(system)
  if (!os) return undefined
  const arch = system?.arch === 'x86_64' || system?.arch === 'amd64' ? 'x64' : system?.arch
  return arch ? `${os} · ${arch}` : os
}

export function unsupportedMessage(t: Translate, machine: SshMachineView): string {
  switch (machine.status.unsupported) {
    case 'windows':
      return t('settings.ssh.unsupported.windows')
    case 'linuxArm':
      return t('settings.ssh.unsupported.linuxArm')
    default:
      return t('settings.ssh.unsupported.other', { system: machine.status.system || machine.system?.os || '?' })
  }
}

export function machineStatusView(t: Translate, machine: SshMachineView): MachineStatusView {
  const { status } = machine
  const label = t(LABEL_KEYS[status.kind])
  switch (status.kind) {
    case 'notConnected':
      return { tone: 'neutral', label }
    case 'connecting':
    case 'installing':
      return { tone: 'pending', label }
    case 'connected': {
      const count = machine.projects.length
      const parts = [
        machine.system?.dotcraftVersion ? `DotCraft ${machine.system.dotcraftVersion}` : undefined,
        osLabel(machine.system),
        count > 0
          ? t(count === 1 ? 'settings.ssh.projectCount.one' : 'settings.ssh.projectCount.other', { count })
          : undefined
      ]
      const message = parts.filter(Boolean).join(' · ')
      return { tone: 'success', label, ...(message ? { message } : {}) }
    }
    case 'notInstalled':
      return { tone: 'warning', label, message: t('settings.ssh.message.notInstalled'), action: 'install' }
    case 'updateRequired':
      return {
        tone: 'warning',
        label,
        message: t('settings.ssh.message.updateRequired', {
          version: status.installedVersion || machine.system?.dotcraftVersion || '?'
        }),
        action: 'update'
      }
    case 'setupModel':
      return { tone: 'warning', label, message: t('settings.ssh.message.setupModel'), action: 'setup' }
    case 'failed':
      return { tone: 'error', label, ...(status.message ? { message: status.message } : {}), action: 'reconnect' }
    case 'unsupported':
      return { tone: 'neutral', label, message: unsupportedMessage(t, machine) }
  }
}

export function badgeTone(machine: SshMachineView): StatusTone {
  const view = machine.status.kind
  if (view === 'connected') return 'success'
  if (view === 'failed') return 'error'
  if (view === 'notInstalled' || view === 'updateRequired' || view === 'setupModel') return 'warning'
  return 'neutral'
}

export function canAddProject(machine: SshMachineView): boolean {
  return machineHasHub(machine.status)
}

export function canReachMachine(machine: SshMachineView): boolean {
  return machineHasHub(machine.status) || machine.status.kind === 'notInstalled' || machine.status.kind === 'updateRequired'
}

export function canOpenProject(machine: SshMachineView): boolean {
  const kind = machine.status.kind
  return kind !== 'unsupported' && kind !== 'installing' && kind !== 'notInstalled' && kind !== 'updateRequired'
}

export function machineHostLabel(machine: SshMachineView): string | undefined {
  if (machine.source === 'manual') return machine.hostname
  return machine.resolved ? formatResolvedHost(machine.resolved) : undefined
}

export function machinePort(machine: SshMachineView): number {
  return machine.source === 'manual' ? machine.port ?? 22 : machine.resolved?.port ?? 22
}

export function displayRemotePath(path: string, home: string | undefined): string {
  if (!home || home === '/') return path
  if (path === home) return '~'
  return path.startsWith(`${home}/`) ? `~${path.slice(home.length)}` : path
}
