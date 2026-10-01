import { useEffect, useState, type JSX } from 'react'
import { Laptop, Plus, Server } from 'lucide-react'

import { SettingsGroup } from '../../SettingsGroup'
import { Button } from '../../../ui/Button'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import { ContextMenu, type ContextMenuEntry, type ContextMenuPosition } from '../../../ui/ContextMenu'
import { useT } from '../../../../contexts/LocaleContext'
import { useSshMachinesStore } from '../../../../stores/sshMachinesStore'
import { addToast } from '../../../../stores/toastStore'
import type { RemoteProject, SshMachineView } from '../../../../../shared/sshMachines'
import { AddSshConnectionDialog } from './AddSshConnectionDialog'
import { RemoteProjectPickerDialog } from './RemoteProjectPickerDialog'
import { SshMachineRow } from './SshMachineRow'
import { SshMachineSettingsDialog } from './SshMachineSettingsDialog'
import { canAddProject, type MachineAction } from './sshMachinePresentation'

type Dialog =
  | { kind: 'add' }
  | { kind: 'settings'; machineId: string }
  | { kind: 'picker'; machineId: string; returnTo?: string; thenSetUp?: boolean }
  | null

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

export function SshMachinesSegment(): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const machines = useSshMachinesStore((s) => s.machines)
  const store = useSshMachinesStore.getState
  const [dialog, setDialog] = useState<Dialog>(null)
  const [menu, setMenu] = useState<{ id: string; position: ContextMenuPosition } | null>(null)

  useEffect(() => {
    useSshMachinesStore.getState().ensureLoaded()
  }, [])

  async function guarded(task: () => Promise<unknown>): Promise<void> {
    try {
      await task()
    } catch (error) {
      addToast(messageOf(error), 'error')
    }
  }

  async function setUp(machineId: string, projectId?: string): Promise<void> {
    const outcome = await store().setUpModel(machineId, projectId)
    if (outcome === 'needsProject') setDialog({ kind: 'picker', machineId, thenSetUp: true })
  }

  async function runAction(machine: SshMachineView, action: MachineAction): Promise<void> {
    if (action === 'install') return store().install(machine.id)
    if (action === 'reconnect') return store().reconnect(machine.id)
    if (action === 'setup') return setUp(machine.id)
    const ok = await confirm({
      title: t('settings.ssh.confirmUpdate.title', { name: machine.name }),
      message: t('settings.ssh.confirmUpdate.message'),
      confirmLabel: t('settings.ssh.action.update'),
      cancelLabel: t('common.cancel')
    })
    if (ok) await store().update(machine.id)
  }

  async function confirmDelete(machine: SshMachineView): Promise<void> {
    const ok = await confirm({
      title: t('settings.ssh.confirmDelete.title', { name: machine.name }),
      message: t('settings.ssh.confirmDelete.message'),
      confirmLabel: t('settings.ssh.delete'),
      cancelLabel: t('common.cancel'),
      danger: true
    })
    if (ok) await store().remove(machine.id)
  }

  function menuItems(machine: SshMachineView): ContextMenuEntry[] {
    const entries: ContextMenuEntry[] = [
      {
        label: t('settings.ssh.menu.addProject'),
        disabled: !canAddProject(machine),
        onClick: () => setDialog({ kind: 'picker', machineId: machine.id })
      },
      { label: t('settings.ssh.menu.settings'), onClick: () => setDialog({ kind: 'settings', machineId: machine.id }) }
    ]
    if (machine.autoConnect && machine.status.kind !== 'unsupported' && machine.status.kind !== 'connecting') {
      entries.push({ label: t('settings.ssh.action.reconnect'), onClick: () => void guarded(() => store().reconnect(machine.id)) })
    }
    entries.push(
      { type: 'separator' },
      { label: t('settings.ssh.delete'), danger: true, onClick: () => void guarded(() => confirmDelete(machine)) }
    )
    return entries
  }

  function closePicker(current: Extract<Dialog, { kind: 'picker' }>): void {
    setDialog(current.returnTo ? { kind: 'settings', machineId: current.returnTo } : null)
  }

  function pickerAdded(current: Extract<Dialog, { kind: 'picker' }>, machineId: string, project: RemoteProject): void {
    closePicker(current)
    if (current.thenSetUp) void guarded(() => setUp(machineId, project.id))
  }

  const addButton = (
    <Button variant="primary" iconLeft={<Plus size={15} aria-hidden />} onClick={() => setDialog({ kind: 'add' })}>
      {t('settings.ssh.add.button')}
    </Button>
  )
  const menuMachine = menu ? machines.find((machine) => machine.id === menu.id) : undefined
  const settingsMachine =
    dialog?.kind === 'settings' ? machines.find((machine) => machine.id === dialog.machineId) : undefined

  return (
    <>
      {machines.length === 0 ? (
        <SettingsGroup title={t('settings.ssh.title')} framed={false}>
          <div className="dc-ssh-empty">
            <span className="dc-ssh-empty__glyph" aria-hidden>
              <Laptop size={18} />
              <span className="dc-ssh-empty__dots">···</span>
              <Server size={18} />
            </span>
            <p className="dc-ssh-empty__copy">{t('settings.ssh.empty')}</p>
            {addButton}
          </div>
        </SettingsGroup>
      ) : (
        <SettingsGroup title={t('settings.ssh.title')} headerAction={addButton}>
          {machines.map((machine) => (
            <SshMachineRow
              key={machine.id}
              machine={machine}
              menuOpen={menu?.id === machine.id}
              onToggle={(on) => void guarded(() => store().setAutoConnect(machine.id, on))}
              onAction={(action) => void guarded(() => runAction(machine, action))}
              onMenu={(anchor) => {
                const rect = anchor.getBoundingClientRect()
                setMenu({ id: machine.id, position: { x: rect.right - 200, y: rect.bottom + 4 } })
              }}
            />
          ))}
        </SettingsGroup>
      )}

      {dialog?.kind === 'add' && <AddSshConnectionDialog onClose={() => setDialog(null)} />}

      {settingsMachine && (
        <SshMachineSettingsDialog
          key={settingsMachine.id}
          machine={settingsMachine}
          onClose={() => setDialog(null)}
          onAddProject={() =>
            setDialog({ kind: 'picker', machineId: settingsMachine.id, returnTo: settingsMachine.id })
          }
        />
      )}

      {dialog?.kind === 'picker' && (
        <RemoteProjectPickerDialog
          initialMachineId={dialog.machineId}
          onClose={() => closePicker(dialog)}
          onAdded={(machineId, project) => pickerAdded(dialog, machineId, project)}
        />
      )}

      {menu && menuMachine && (
        <ContextMenu items={menuItems(menuMachine)} position={menu.position} onClose={() => setMenu(null)} />
      )}
    </>
  )
}
