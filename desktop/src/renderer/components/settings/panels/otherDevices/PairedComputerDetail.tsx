import { useState, type JSX } from 'react'
import { ArrowUpRight, Folder, MessagesSquare, Plus, RefreshCw, Trash2 } from 'lucide-react'

import { SettingsBreadcrumb } from '../../SettingsBreadcrumb'
import { SettingsGroup } from '../../SettingsGroup'
import { SettingsPageHeader } from '../../SettingsPageHeader'
import { ActionTooltip } from '../../../ui/ActionTooltip'
import { Button } from '../../../ui/Button'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import type { ContextMenuEntry } from '../../../ui/ContextMenu'
import { IconButton } from '../../../ui/IconButton'
import { StatusMenuButton, type StatusMenuTone } from '../../../ui/StatusMenuButton'
import { useT } from '../../../../contexts/LocaleContext'
import { usePairedComputersStore } from '../../../../stores/pairedComputersStore'
import { addToast } from '../../../../stores/toastStore'
import { useUIStore } from '../../../../stores/uiStore'
import type { PairedComputer, PairedComputerProject } from '../../../../../shared/pairedComputers'
import { PhonesNotice } from '../phones/PhonesSegment'
import { AddFolderDialog } from './AddFolderDialog'
import { STATUS_LABEL, STATUS_TONE, projectsBlockedReason, statusMessage } from './pairedComputerPresentation'

function ProjectRow({
  computer,
  project,
  blockedReason
}: {
  computer: PairedComputer
  project: PairedComputerProject
  blockedReason?: string
}): JSX.Element {
  const t = useT()
  const store = usePairedComputersStore.getState
  const unavailableReason = project.unavailable ? t('settings.otherDevices.projects.unavailable') : undefined
  const openBlockedReason = unavailableReason ?? blockedReason

  async function removeFromProjects(): Promise<void> {
    const ok = await store().removeProject(computer.computerId, project.projectId)
    if (!ok) addToast(t('settings.otherDevices.projects.saveFailed'), 'error')
  }

  async function open(): Promise<void> {
    const ok = await store().openProject(computer.computerId, project.projectId)
    if (ok) useUIStore.getState().setActiveMainView('conversation')
    else addToast(t('settings.otherDevices.projects.openFailed', { name: project.displayName }), 'error')
  }

  return (
    <div className="dc-other-device-project" data-unavailable={project.unavailable ? 'true' : undefined}>
      <span className="dc-other-device-project__icon" aria-hidden>
        {project.kind === 'chats' ? <MessagesSquare size={16} /> : <Folder size={16} />}
      </span>
      <span className="dc-other-device-project__text">
        <span className="dc-other-device-project__name">{project.displayName}</span>
        {project.unavailable ? (
          <span className="dc-other-device-project__path">{t('settings.otherDevices.projects.unavailable')}</span>
        ) : (
          project.kind === 'project' && <span className="dc-other-device-project__path">{project.path}</span>
        )}
      </span>
      <span className="dc-other-device-project__actions">
        <IconButton
          icon={<ArrowUpRight size={16} aria-hidden />}
          label={t('settings.otherDevices.projects.openNamed', { name: project.displayName })}
          tooltipLabel={t('settings.otherDevices.projects.open')}
          disabled={openBlockedReason != null}
          disabledReason={openBlockedReason}
          onClick={() => void open()}
        />
        <IconButton
          icon={<Trash2 size={15} aria-hidden />}
          label={t('settings.otherDevices.projects.removeNamed', { name: project.displayName })}
          tooltipLabel={t('settings.otherDevices.projects.removeFromProjects')}
          disabled={!project.saved}
          disabledReason={project.saved ? undefined : t('settings.otherDevices.projects.notAdded')}
          onClick={() => void removeFromProjects()}
        />
      </span>
    </div>
  )
}

export function PairedComputerDetail({
  computer,
  onBack
}: {
  computer: PairedComputer
  onBack: () => void
}): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const [adding, setAdding] = useState(false)
  const [removing, setRemoving] = useState(false)
  const store = usePairedComputersStore.getState
  const blockedReason = projectsBlockedReason(t, computer)
  const identityChanged = computer.status === 'identityChanged'
  const tone = STATUS_TONE[computer.status]
  const description = identityChanged ? undefined : statusMessage(t, computer) ?? undefined

  async function remove(): Promise<void> {
    const confirmed = await confirm({
      title: t('settings.phones.remove.title', { name: computer.name }),
      message: t('settings.otherDevices.remove.message', { name: computer.name }),
      confirmLabel: t('settings.phones.remove'),
      cancelLabel: t('common.cancel'),
      danger: true
    })
    if (!confirmed) return
    setRemoving(true)
    const removed = await store().remove(computer.computerId)
    setRemoving(false)
    if (removed) onBack()
    else addToast(t('settings.phones.remove.failed', { name: computer.name }), 'error')
  }

  const menuItems: ContextMenuEntry[] = [
    {
      label: t('settings.connections.refresh'),
      icon: <RefreshCw size={14} aria-hidden />,
      onClick: () => void window.api.pairedComputers.refresh()
    },
    { type: 'separator' },
    {
      label: t('settings.phones.remove'),
      icon: <Trash2 size={14} aria-hidden />,
      danger: true,
      onClick: () => void remove()
    }
  ]

  const addFolder = (
    <ActionTooltip label="" disabledReason={blockedReason}>
      <Button
        variant="secondary"
        iconLeft={<Plus size={15} aria-hidden />}
        disabled={blockedReason != null}
        onClick={() => setAdding(true)}
      >
        {t('settings.otherDevices.projects.addFolder')}
      </Button>
    </ActionTooltip>
  )

  return (
    <div className="dc-other-device-detail">
      <SettingsPageHeader
        title={computer.name}
        breadcrumb={
          <SettingsBreadcrumb
            parentLabel={t('settings.connections.segments.otherDevices')}
            currentLabel={computer.name}
            onBack={onBack}
          />
        }
        description={description}
        action={
          identityChanged ? undefined : (
            <StatusMenuButton
              label={t(STATUS_LABEL[computer.status])}
              tone={tone === 'pending' ? 'neutral' : (tone as StatusMenuTone)}
              items={menuItems}
              loading={removing}
            />
          )
        }
      />

      {identityChanged && (
        <PhonesNotice
          title={t('settings.otherDevices.status.identityChanged')}
          reason={statusMessage(t, computer) ?? ''}
          action={
            <Button variant="secondary" loading={removing} onClick={() => void remove()}>
              {t('settings.phones.remove')}
            </Button>
          }
        />
      )}

      {!identityChanged &&
        (computer.projects.length === 0 ? (
          <SettingsGroup title={t('settings.otherDevices.projects.title')} headerAction={addFolder}>
            <p className="dc-other-device-empty">{blockedReason}</p>
          </SettingsGroup>
        ) : (
          <SettingsGroup title={t('settings.otherDevices.projects.title')} headerAction={addFolder}>
            {computer.projects.map((project) => (
              <ProjectRow
                key={project.projectId}
                computer={computer}
                project={project}
                blockedReason={blockedReason}
              />
            ))}
          </SettingsGroup>
        ))}

      {adding && <AddFolderDialog computer={computer} onClose={() => setAdding(false)} />}
    </div>
  )
}
