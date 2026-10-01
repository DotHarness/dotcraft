import { useRef, type JSX } from 'react'

import { Button } from '../../../ui/Button'
import { MoreActionsButton } from '../../../ui/MoreActionsButton'
import { PillSwitch } from '../../../ui/PillSwitch'
import { StatusIndicator } from '../../../ui/StatusIndicator'
import { useT } from '../../../../contexts/LocaleContext'
import type { SshMachineView } from '../../../../../shared/sshMachines'
import { ACTION_KEYS, machineStatusView, type MachineAction } from './sshMachinePresentation'

export function SshMachineStatusLine({ machine }: { machine: SshMachineView }): JSX.Element {
  const t = useT()
  const view = machineStatusView(t, machine)
  return (
    <span className="dc-ssh-status">
      <StatusIndicator tone={view.tone} />
      <span className="dc-ssh-status__text">
        {view.label}
        {view.message ? <span> · {view.message}</span> : null}
      </span>
    </span>
  )
}

export function SshMachineRow({
  machine,
  menuOpen,
  onToggle,
  onAction,
  onMenu
}: {
  machine: SshMachineView
  menuOpen: boolean
  onToggle: (on: boolean) => void
  onAction: (action: MachineAction) => void
  onMenu: (anchor: HTMLElement) => void
}): JSX.Element {
  const t = useT()
  const view = machineStatusView(t, machine)
  const menuButton = useRef<HTMLButtonElement>(null)
  return (
    <div className="dc-ssh-row" data-status={machine.status.kind}>
      <span className="dc-ssh-row__switch">
        <PillSwitch
          checked={machine.autoConnect}
          disabled={machine.status.kind === 'unsupported'}
          onChange={onToggle}
          aria-label={t('settings.ssh.keepConnected', { name: machine.name })}
        />
      </span>
      <span className="dc-ssh-row__body">
        <span className="dc-ssh-row__name">{machine.name}</span>
        <SshMachineStatusLine machine={machine} />
      </span>
      <span className="dc-ssh-row__actions">
        {view.action && (
          <Button variant="secondary" onClick={() => onAction(view.action!)}>
            {t(ACTION_KEYS[view.action])}
          </Button>
        )}
        <MoreActionsButton
          ref={menuButton}
          label={t('settings.ssh.moreActionsFor', { name: machine.name })}
          open={menuOpen}
          onClick={() => menuButton.current && onMenu(menuButton.current)}
        />
      </span>
    </div>
  )
}
