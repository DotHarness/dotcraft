import { memo, useMemo, useState, type KeyboardEvent, type MouseEvent } from 'react'
import { ChevronDown, ChevronUp, Redo2, Undo2 } from 'lucide-react'
import type { MessageKey } from '../../../shared/locales'
import { useT } from '../../contexts/LocaleContext'
import { useTurnDiffActions, type TurnPatchOutcome } from '../../hooks/useTurnDiffActions'
import { useConversationStore } from '../../stores/conversationStore'
import { showToast, type ToastType } from '../../stores/toastStore'
import { turnPatchTotals } from '../../stores/turnDiffs'
import { useUIStore } from '../../stores/uiStore'
import type { TurnFileChange } from '../../types/turnDiff'
import { basename } from '../../utils/path'
import { toWorkspaceRelativePath } from '../../utils/workspacePaths'
import { ChangePath } from '../detail/changes/ChangePath'
import { ActionTooltip } from '../ui/ActionTooltip'
import { Button } from '../ui/Button'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { FileDiffStats } from './FileDiffStats'
import { InlineDiffView } from './InlineDiffView'
import styles from './TurnCompletionSummary.module.css'

interface TurnCompletionSummaryProps {
  turnId: string
}

const NO_ROWS: TurnFileChange[] = []
const COLLAPSED_ROW_LIMIT = 3

export const TurnCompletionSummary = memo(function TurnCompletionSummary({ turnId }: TurnCompletionSummaryProps): JSX.Element | null {
  const t = useT()
  const rows = useConversationStore((s) => s.turnDiffs.get(turnId)?.files ?? NO_ROWS)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const totals = useMemo(() => turnPatchTotals(rows), [rows])
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set())
  const [showAll, setShowAll] = useState(false)

  if (rows.length === 0) return null

  function toggleRow(key: string): void {
    setExpanded((current) => {
      const next = new Set(current)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
  }

  const title = totals.files === 1
    ? t('turnChanges.editedFile', { file: basename(rows[0].diff.filePath) })
    : t('turnChanges.editedFiles', { count: totals.files })
  const visibleRows = showAll ? rows : rows.slice(0, COLLAPSED_ROW_LIMIT)
  const hiddenCount = rows.length - visibleRows.length

  return (
    <section className={styles.card} aria-label={title}>
      <div className={styles.header}>
        <div className={styles.heading}>
          <span className={styles.title}>{title}</span>
          <span className={styles.stats}>
            <FileDiffStats additions={totals.additions} deletions={totals.deletions} />
          </span>
        </div>
        <div className={styles.actions}>
          <TurnPatchButton turnId={turnId} rows={rows} workspacePath={workspacePath} />
          <Button variant="ghost" size="sm" onClick={() => useUIStore.getState().showChangesForKey(rows[0].key)}>
            {t('turnChanges.review')}
          </Button>
        </div>
      </div>

      {totals.files > 1 && (
        <>
          <div role="list">
            {visibleRows.map((row) => (
              <TurnFileRow
                key={row.key}
                row={row}
                workspacePath={workspacePath}
                expanded={expanded.has(row.key)}
                onToggle={() => toggleRow(row.key)}
              />
            ))}
          </div>
          {rows.length > COLLAPSED_ROW_LIMIT && (
            <button
              type="button"
              className={styles.moreToggle}
              aria-expanded={showAll}
              onClick={() => setShowAll((current) => !current)}
            >
              {showAll
                ? t('turnChanges.collapseFiles')
                : t(hiddenCount === 1 ? 'turnChanges.showMoreFiles.one' : 'turnChanges.showMoreFiles.other', { count: hiddenCount })}
            </button>
          )}
        </>
      )}
    </section>
  )
})

function outcomeToast(outcome: Exclude<TurnPatchOutcome, 'not-git-repo'>, undo: boolean): { key: MessageKey; type: ToastType } {
  switch (outcome) {
    case 'reverted': return { key: 'turnChanges.toast.reverted', type: 'success' }
    case 'reapplied': return { key: 'turnChanges.toast.reapplied', type: 'success' }
    case 'partial': return { key: undo ? 'turnChanges.toast.revertPartial' : 'turnChanges.toast.reapplyPartial', type: 'warning' }
    case 'failed': return { key: undo ? 'turnChanges.toast.revertFailed' : 'turnChanges.toast.reapplyFailed', type: 'error' }
  }
}

function TurnPatchButton({
  turnId,
  rows,
  workspacePath
}: {
  turnId: string
  rows: TurnFileChange[]
  workspacePath: string
}): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const { revertTurn, reapplyTurn } = useTurnDiffActions(workspacePath)
  const [pending, setPending] = useState(false)
  // Truncated rows are never applied, so only the rest decide between Undo and Reapply.
  const applicable = rows.filter((row) => !row.truncated)
  const undo = applicable.some((row) => row.diff.status === 'written')

  async function handleClick(): Promise<void> {
    setPending(true)
    const outcome = await (undo ? revertTurn(turnId) : reapplyTurn(turnId))
    setPending(false)
    if (outcome === 'not-git-repo') {
      void confirm({
        title: t(undo ? 'turnChanges.notGitRepo.undoTitle' : 'turnChanges.notGitRepo.reapplyTitle'),
        message: t('turnChanges.notGitRepo.message'),
        confirmLabel: t('common.close'),
        alert: true
      })
      return
    }
    const toast = outcomeToast(outcome, undo)
    showToast({ message: t(toast.key), type: toast.type, key: 'turn-patch' })
  }

  return (
    <Button
      variant="ghost"
      size="sm"
      disabled={pending || applicable.length === 0}
      onClick={() => { void handleClick() }}
    >
      {undo ? t('turnChanges.undo') : t('turnChanges.reapply')}
      {undo
        ? <Undo2 size={14} strokeWidth={1.8} aria-hidden />
        : <Redo2 size={14} strokeWidth={1.8} aria-hidden />}
    </Button>
  )
}

function TurnFileRow({
  row,
  workspacePath,
  expanded,
  onToggle
}: {
  row: TurnFileChange
  workspacePath: string
  expanded: boolean
  onToggle: () => void
}): JSX.Element {
  const t = useT()
  const file = row.diff

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
    if (event.key !== 'Enter' && event.key !== ' ') return
    event.preventDefault()
    onToggle()
  }

  function openInChanges(event: MouseEvent<HTMLButtonElement>): void {
    event.stopPropagation()
    useUIStore.getState().showChangesForKey(row.key)
  }

  return (
    <div role="listitem" className={styles.item}>
      <div
        role="button"
        tabIndex={0}
        aria-expanded={expanded}
        className={styles.row}
        onClick={onToggle}
        onKeyDown={handleKeyDown}
      >
        <span className={styles.path}>
          <button type="button" className={styles.pathLink} onClick={openInChanges}>
            <ChangePath path={toWorkspaceRelativePath(workspacePath, file.filePath)} />
          </button>
          {file.isNewFile && <NewFileDot label={t('diffViewer.newFile')} />}
        </span>
        <FileDiffStats
          additions={file.additions}
          deletions={file.deletions}
          tone={file.status === 'reverted' ? 'dimmed' : 'semantic'}
        />
        <span className={styles.chevron}>
          {expanded ? <ChevronUp size={15} strokeWidth={1.8} /> : <ChevronDown size={15} strokeWidth={1.8} />}
        </span>
      </div>
      {expanded && (
        <div className={styles.diff}>
          <InlineDiffView diff={file} variant="embedded" presentation="body-only" />
        </div>
      )}
    </div>
  )
}

function NewFileDot({ label }: { label: string }): JSX.Element {
  return (
    <ActionTooltip label={label}>
      <span role="img" aria-label={label} className={styles.newFileDot} />
    </ActionTooltip>
  )
}
