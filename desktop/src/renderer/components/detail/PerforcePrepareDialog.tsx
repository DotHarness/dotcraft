import { useEffect, useState } from 'react'
import { ListChecks } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { changelistLabel, type PerforceChangelistEntry } from '../../stores/perforceChangelistStore'
import { Select, type SelectOption } from '../ui/Select'
import { ChangeSubmitDialog } from './ChangeSubmitDialog'

const NEW_CHANGELIST_VALUE = '__new_changelist__'

interface PerforcePrepareDialogProps {
  workspacePath: string
  changelist: string
  changelists: PerforceChangelistEntry[]
  onPrepare: (description: string, target: string) => void
  onClose: () => void
}

export function PerforcePrepareDialog({
  workspacePath,
  changelist,
  changelists,
  onPrepare,
  onClose
}: PerforcePrepareDialogProps): JSX.Element {
  const t = useT()
  const [targetChoice, setTargetChoice] = useState(initialTargetChoice(changelist))

  useEffect(() => {
    setTargetChoice(initialTargetChoice(changelist))
  }, [changelist])

  const targetEntries = normalizeChangelists(changelists, changelist)
  const targetOptions: SelectOption[] = [
    { value: NEW_CHANGELIST_VALUE, label: t('perforcePrepare.newChangelist') },
    ...targetEntries.map((entry) => ({
      value: entry.id,
      label: changelistOptionLabel(entry)
    }))
  ]

  return (
    <ChangeSubmitDialog
      icon={<ListChecks size={18} aria-hidden="true" />}
      workspacePath={workspacePath}
      target={{
        label: t('perforcePrepare.targetLabel'),
        control: true,
        value: (
          <Select
            ariaLabel={t('perforcePrepare.targetLabel')}
            appearance="frameless"
            value={targetChoice}
            options={targetOptions}
            onValueChange={setTargetChoice}
          />
        )
      }}
      copy={{
        title: t('perforcePrepare.title'),
        close: t('perforcePrepare.close'),
        changesLabel: t('perforcePrepare.changesLabel'),
        changesSummary: (files) => t('perforcePrepare.changesSummary', { files }),
        filesHeader: ({ written, all, reverted }) => t('perforcePrepare.filesHeader', {
          written,
          all,
          reverted: reverted > 0 ? t('perforcePrepare.revertedSuffix', { count: reverted }) : ''
        }),
        inputLabel: t('perforcePrepare.descriptionLabel'),
        placeholder: t('perforcePrepare.placeholder'),
        submit: t('perforcePrepare.button')
      }}
      onSubmit={(description) => onPrepare(description, targetChoice === NEW_CHANGELIST_VALUE ? 'default' : targetChoice)}
      onClose={onClose}
    />
  )
}

function initialTargetChoice(changelist: string): string {
  return changelistLabel(changelist) === 'default' ? NEW_CHANGELIST_VALUE : changelistLabel(changelist)
}

function normalizeChangelists(
  changelists: PerforceChangelistEntry[],
  selectedChangelist: string
): PerforceChangelistEntry[] {
  const entries = changelists.length > 0
    ? changelists
    : [{ id: 'default', isDefault: true, description: '', user: '', client: '', status: 'pending' }]
  const selected = changelistLabel(selectedChangelist)
  const withSelected = entries.some((entry) => entry.id === selected)
    ? entries
    : [
        ...entries,
        { id: selected, isDefault: selected === 'default', description: '', user: '', client: '', status: 'pending' }
      ]
  return [...withSelected]
    .filter((entry, index, all) => all.findIndex((candidate) => candidate.id === entry.id) === index)
    .sort((a, b) => {
      if (a.id === 'default') return -1
      if (b.id === 'default') return 1
      const left = Number(a.id)
      const right = Number(b.id)
      return Number.isFinite(left) && Number.isFinite(right)
        ? left - right
        : a.id.localeCompare(b.id)
    })
}

function changelistOptionLabel(entry: PerforceChangelistEntry): string {
  const label = changelistLabel(entry.id)
  const description = entry.description.trim()
  return description && label !== 'default' ? `${label} - ${description}` : label
}
