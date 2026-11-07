import { useId, useState, type JSX } from 'react'
import { FolderPlus } from 'lucide-react'

import { Button } from '../../../ui/Button'
import { Input } from '../../../ui/Input'
import { ModalHeader } from '../../../ui/ModalHeader'
import { useT } from '../../../../contexts/LocaleContext'
import { usePairedComputersStore } from '../../../../stores/pairedComputersStore'
import type { MessageKey } from '../../../../../shared/locales'
import type { AddFolderFailureCode, PairedComputer } from '../../../../../shared/pairedComputers'
import { PhonesDialog, PhonesDialogFailure } from '../phones/PhonesDialog'

const FAILURE_KEY: Record<AddFolderFailureCode, MessageKey> = {
  relativePath: 'settings.otherDevices.folder.failure.relative',
  workspaceNotFound: 'settings.otherDevices.folder.failure.missing',
  unreachable: 'settings.otherDevices.folder.failure.unreachable'
}

export function AddFolderDialog({
  computer,
  onClose
}: {
  computer: PairedComputer
  onClose: () => void
}): JSX.Element {
  const t = useT()
  const titleId = useId()
  const pathId = useId()
  const [path, setPath] = useState('')
  const [saving, setSaving] = useState(false)
  const [failure, setFailure] = useState<AddFolderFailureCode | null>(null)
  const value = path.trim()

  async function submit(): Promise<void> {
    if (!value || saving) return
    setSaving(true)
    setFailure(null)
    const result = await usePairedComputersStore.getState().addFolder(computer.computerId, value)
    setSaving(false)
    if (result) setFailure(result)
    else onClose()
  }

  return (
    <PhonesDialog
      titleId={titleId}
      onClose={onClose}
      onSubmit={() => void submit()}
      header={(
        <ModalHeader
          icon={<FolderPlus size={18} aria-hidden />}
          title={t('settings.otherDevices.projects.addFolder')}
          titleId={titleId}
          description={t('settings.otherDevices.folder.description', { name: computer.name })}
          onClose={onClose}
          closeLabel={t('common.close')}
        />
      )}
      footer={(
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button type="submit" variant="primary" loading={saving} disabled={!value}>
            {t('settings.otherDevices.projects.add')}
          </Button>
        </>
      )}
    >
      <div className="dc-phones-relay-form__field">
        <label className="dc-satellite-field__label" htmlFor={pathId}>
          {t('settings.otherDevices.folder.label')}
        </label>
        <Input
          id={pathId}
          data-autofocus
          mono
          value={path}
          placeholder="/home/you/project"
          onChange={(event) => {
            setPath(event.target.value)
            setFailure(null)
          }}
        />
      </div>
      {failure && (
        <PhonesDialogFailure
          title={t('settings.otherDevices.folder.failure.title')}
          reason={t(FAILURE_KEY[failure], { name: computer.name })}
        />
      )}
    </PhonesDialog>
  )
}
