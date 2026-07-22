import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { ChevronDown, ChevronRight, ListChecks } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { changelistLabel, type PerforceChangelistEntry } from '../../stores/perforceChangelistStore'
import { ModalHeader } from '../ui/ModalHeader'
import { Button } from '../ui/Button'
import { Select, type SelectOption } from '../ui/Select'
import { toRelativePath } from './CommitDialog'

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
  const changedFiles = useConversationStore((s) => s.changedFiles)
  const allFiles = Array.from(changedFiles.values())
  const writtenFiles = allFiles.filter((f) => f.status === 'written')
  const revertedCount = allFiles.length - writtenFiles.length
  const [description, setDescription] = useState('')
  const [filesExpanded, setFilesExpanded] = useState(false)
  const [targetChoice, setTargetChoice] = useState(initialTargetChoice(changelist))
  const descriptionRef = useRef<HTMLTextAreaElement>(null)
  const totalAdditions = writtenFiles.reduce((sum, file) => sum + file.additions, 0)
  const totalDeletions = writtenFiles.reduce((sum, file) => sum + file.deletions, 0)
  const hasFiles = writtenFiles.length > 0

  useEffect(() => {
    descriptionRef.current?.focus()
    function handleKeyDown(e: KeyboardEvent): void {
      if (e.defaultPrevented) return
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  useEffect(() => {
    setTargetChoice(initialTargetChoice(changelist))
  }, [changelist])

  function handleSubmit(): void {
    if (!hasFiles) return
    onPrepare(description.trim(), targetChoice === NEW_CHANGELIST_VALUE ? 'default' : targetChoice)
    onClose()
  }

  const targetEntries = normalizeChangelists(changelists, changelist)
  const targetOptions: SelectOption[] = [
    { value: NEW_CHANGELIST_VALUE, label: t('perforcePrepare.newChangelist') },
    ...targetEntries.map((entry) => ({
      value: entry.id,
      label: changelistOptionLabel(entry)
    }))
  ]

  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="perforce-prepare-title"
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 10000,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'var(--overlay-scrim)'
      }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose()
      }}
    >
      <div
        style={{
          backgroundColor: 'var(--bg-secondary)',
          borderRadius: '16px',
          boxShadow: 'var(--shadow-level-3)',
          padding: '18px 24px 22px',
          width: '440px',
          maxWidth: 'calc(100vw - 48px)',
          maxHeight: 'calc(100vh - 96px)',
          overflow: 'auto'
        }}
        onMouseDown={(e) => e.stopPropagation()}
      >
        <ModalHeader
          icon={<ListChecks size={18} aria-hidden="true" />}
          title={t('perforcePrepare.title')}
          titleId="perforce-prepare-title"
          onClose={onClose}
          closeLabel={t('perforcePrepare.close')}
        />

        <div style={{ ...infoRowStyle, gap: '12px' }}>
          <span style={infoLabelStyle}>{t('perforcePrepare.targetLabel')}</span>
          <span style={{ flex: 1, minWidth: 0 }}>
            <Select
              ariaLabel={t('perforcePrepare.targetLabel')}
              appearance="frameless"
              value={targetChoice}
              options={targetOptions}
              onValueChange={setTargetChoice}
            />
          </span>
        </div>

        <button
          type="button"
          onClick={() => setFilesExpanded((v) => !v)}
          aria-label={filesExpanded ? t('perforcePrepare.collapseFiles') : t('perforcePrepare.expandFiles')}
          style={{
            ...infoRowStyle,
            width: '100%',
            border: 'none',
            background: 'transparent',
            cursor: 'pointer',
            textAlign: 'left',
            fontFamily: 'inherit'
          }}
        >
          <span style={infoLabelStyle}>{t('perforcePrepare.changesLabel')}</span>
          <span style={{ ...infoValueStyle, gap: '8px' }}>
            <span>{t('perforcePrepare.changesSummary', { files: writtenFiles.length })}</span>
            <span style={{ color: 'var(--success)' }}>+{totalAdditions}</span>
            <span style={{ color: 'var(--error)' }}>-{totalDeletions}</span>
            {filesExpanded ? <ChevronDown size={15} /> : <ChevronRight size={15} />}
          </span>
        </button>

        {filesExpanded && (
          <div
            style={{
              background: 'var(--bg-primary)',
              borderRadius: '10px',
              padding: '2px 4px',
              margin: '2px 0 4px',
              maxHeight: '180px',
              overflowY: 'auto'
            }}
          >
            <div style={{ fontSize: '11px', color: 'var(--text-secondary)', padding: '6px 8px 2px' }}>
              {t('perforcePrepare.filesHeader', {
                written: writtenFiles.length,
                all: allFiles.length,
                reverted:
                  revertedCount > 0 ? t('perforcePrepare.revertedSuffix', { count: revertedCount }) : ''
              })}
            </div>
            {writtenFiles.map((file, idx) => (
              <div
                key={file.filePath}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '8px',
                  padding: '6px 8px',
                  borderTop: idx === 0 ? 'none' : '1px solid var(--glass-border)',
                  fontSize: '12px'
                }}
              >
                <span
                  style={{
                    width: '7px',
                    height: '7px',
                    borderRadius: '50%',
                    background: 'var(--info)',
                    flexShrink: 0
                  }}
                />
                <span
                  style={{
                    flex: 1,
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                    fontFamily: 'var(--font-mono)',
                    color: 'var(--text-primary)'
                  }}
                >
                  {toRelativePath(file.filePath, workspacePath)}
                </span>
                <span style={{ display: 'flex', gap: '5px', fontFamily: 'var(--font-mono)', fontSize: '11px', flexShrink: 0 }}>
                  {file.additions > 0 && <span style={{ color: 'var(--success)' }}>+{file.additions}</span>}
                  {file.deletions > 0 && <span style={{ color: 'var(--error)' }}>-{file.deletions}</span>}
                </span>
              </div>
            ))}
          </div>
        )}

        <div style={{ margin: '12px 0 6px', fontSize: '12px', color: 'var(--text-secondary)' }}>
          {t('perforcePrepare.descriptionLabel')}
        </div>

        <textarea
          ref={descriptionRef}
          className="dc-dialog-input"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          onKeyDown={(e) => {
            if ((e.metaKey || e.ctrlKey) && e.key === 'Enter') {
              e.preventDefault()
              handleSubmit()
            }
          }}
          rows={3}
          placeholder={t('perforcePrepare.placeholder')}
        />

        <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '16px' }}>
          <Button
            variant="primary"
            onClick={handleSubmit}
            disabled={!hasFiles}
          >
            {t('perforcePrepare.button')}
          </Button>
        </div>
      </div>
    </div>
  )

  return createPortal(dialog, document.body) as JSX.Element
}

const infoRowStyle: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  padding: '9px 2px',
  fontSize: '13px'
}

const infoLabelStyle: React.CSSProperties = {
  color: 'var(--text-secondary)'
}

const infoValueStyle: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '6px',
  color: 'var(--text-primary)',
  fontFamily: 'var(--font-mono)'
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
