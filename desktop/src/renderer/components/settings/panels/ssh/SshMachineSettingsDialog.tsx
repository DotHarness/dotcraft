import { useId, useState, type JSX, type ReactNode } from 'react'
import { Folder, FolderPlus, Pencil, Server, X } from 'lucide-react'

import { Button } from '../../../ui/Button'
import { IconButton } from '../../../ui/IconButton'
import { ModalHeader } from '../../../ui/ModalHeader'
import { useT } from '../../../../contexts/LocaleContext'
import { useSshMachinesStore } from '../../../../stores/sshMachinesStore'
import { addToast } from '../../../../stores/toastStore'
import type { SshMachineValidation, SshMachineView } from '../../../../../shared/sshMachines'
import { SshDialogFrame } from './SshDialogFrame'
import { SshDockerDeployments, useDockerDeployments } from './SshDockerDeployments'
import { SshMachineForm, parseMachineDraft, type MachineDraft } from './SshMachineForm'
import {
  badgeTone,
  canAddProject,
  canOpenProject,
  displayRemotePath,
  machineHostLabel,
  machinePort,
  machineStatusView,
  systemLabel
} from './sshMachinePresentation'

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

function draftFor(machine: SshMachineView): MachineDraft {
  return {
    name: machine.name,
    hostname: machine.hostname ?? '',
    port: machine.port ? String(machine.port) : '',
    auth: machine.identityFile ? 'identity' : 'agent',
    identityFile: machine.identityFile ?? ''
  }
}

export function SshMachineSettingsDialog({
  machine,
  onClose,
  onAddProject
}: {
  machine: SshMachineView
  onClose: () => void
  onAddProject: () => void
}): JSX.Element {
  const t = useT()
  const titleId = useId()
  const machines = useSshMachinesStore((s) => s.machines)
  const edit = useSshMachinesStore((s) => s.edit)
  const removeProject = useSshMachinesStore((s) => s.removeProject)
  const openProject = useSshMachinesStore((s) => s.openProject)
  const [editing, setEditing] = useState(false)
  const [serverErrors, setServerErrors] = useState<SshMachineValidation | undefined>()
  const connected = canAddProject(machine)
  const stacks = useDockerDeployments(machine, connected)
  const manual = machine.source === 'manual'

  const identity: ReactNode = manual
    ? machine.identityFile
      ? <code>{machine.identityFile}</code>
      : t('settings.ssh.form.authAgent')
    : t('settings.ssh.facts.identityFromConfig')
  const version =
    machine.system?.dotcraftVersion ??
    (machine.status.kind === 'notInstalled' ? t('settings.ssh.status.notInstalled') : '—')
  const rows: [string, ReactNode][] = [
    ...(manual ? [] : [[t('settings.ssh.facts.alias'), machine.alias ?? '—'] as [string, ReactNode]]),
    [t('settings.ssh.facts.host'), machineHostLabel(machine) ?? '—'],
    [t('settings.ssh.facts.port'), String(machinePort(machine))],
    [t('settings.ssh.facts.identity'), identity],
    [t('settings.ssh.facts.system'), systemLabel(machine.system) ?? '—'],
    [t('settings.ssh.facts.version'), version]
  ]

  async function save(draft: MachineDraft): Promise<void> {
    const parsed = parseMachineDraft(draft)
    if ('portError' in parsed) return
    try {
      const result = await edit(
        machine.id,
        manual
          ? {
              name: parsed.name,
              hostname: parsed.hostname,
              port: parsed.port ?? null,
              identityFile: parsed.identityFile ?? null
            }
          : { name: parsed.name }
      )
      if (result.ok) {
        setServerErrors(undefined)
        setEditing(false)
      } else {
        setServerErrors(result.errors)
      }
    } catch (error) {
      addToast(messageOf(error), 'error')
    }
  }

  async function guarded(task: () => Promise<unknown>): Promise<void> {
    try {
      await task()
    } catch (error) {
      addToast(messageOf(error), 'error')
    }
  }

  return (
    <SshDialogFrame titleId={titleId} wide onClose={onClose}>
      <ModalHeader
        icon={<Server size={18} aria-hidden />}
        title={machine.name}
        titleId={titleId}
        titleAdornment={
          <span className="dc-status-badge" data-size="compact" data-tone={badgeTone(machine) === 'neutral' ? undefined : badgeTone(machine)}>
            <span className="dc-status-badge__label">{machineStatusView(t, machine).label}</span>
          </span>
        }
        onClose={onClose}
        closeLabel={t('common.close')}
        actions={
          editing ? null : (
            <IconButton
              icon={<Pencil size={15} aria-hidden />}
              label={t('settings.ssh.edit')}
              size={30}
              onClick={() => setEditing(true)}
            />
          )
        }
      />
      {editing ? (
        <SshMachineForm
          initial={draftFor(machine)}
          machines={machines}
          exceptId={machine.id}
          nameOnly={!manual}
          submitLabel={t('settings.ssh.save')}
          serverErrors={serverErrors}
          onBack={() => {
            setServerErrors(undefined)
            setEditing(false)
          }}
          onSubmit={save}
        />
      ) : (
        <>
          <dl className="dc-ssh-facts">
            {rows.map(([label, value]) => (
              <div key={label} className="dc-ssh-facts__row">
                <dt>{label}</dt>
                <dd>{value}</dd>
              </div>
            ))}
          </dl>

          <section className="dc-ssh-section">
            <header className="dc-ssh-section__head">
              <h3>{t('settings.ssh.projects.title')}</h3>
              <Button
                variant="secondary"
                iconLeft={<FolderPlus size={15} aria-hidden />}
                disabled={!connected}
                onClick={onAddProject}
              >
                {t('settings.ssh.projects.add')}
              </Button>
            </header>
            {machine.projects.length === 0 ? (
              <p className="dc-ssh-section__empty">
                {t(connected ? 'settings.ssh.projects.empty' : 'settings.ssh.projects.connectFirst')}
              </p>
            ) : (
              <ul className="dc-ssh-projects">
                {machine.projects.map((project) => {
                  const path = displayRemotePath(project.path, machine.system?.home)
                  return (
                    <li key={project.id} className="dc-ssh-project">
                      <Folder size={15} aria-hidden className="dc-ssh-project__icon" />
                      <span className="dc-ssh-project__path" title={project.path}>
                        {path}
                      </span>
                      <Button
                        variant="ghost"
                        disabled={!canOpenProject(machine)}
                        onClick={() => void guarded(() => openProject(machine.id, project.id))}
                      >
                        {t('settings.ssh.open')}
                      </Button>
                      <IconButton
                        icon={<X size={14} aria-hidden />}
                        label={t('settings.ssh.projects.remove', { path })}
                        size={28}
                        onClick={() => void guarded(() => removeProject(machine.id, project.id))}
                      />
                    </li>
                  )
                })}
              </ul>
            )}
          </section>

          {stacks.length > 0 && <SshDockerDeployments machine={machine} stacks={stacks} />}
        </>
      )}
    </SshDialogFrame>
  )
}
