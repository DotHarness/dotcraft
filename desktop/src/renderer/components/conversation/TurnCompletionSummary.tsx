import { memo, useMemo, useState, type KeyboardEvent, type MouseEvent } from 'react'
import { createLucideIcon, Redo2, Undo2 } from 'lucide-react'
import type { MessageKey } from '../../../shared/locales'
import { useT } from '../../contexts/LocaleContext'
import { useTurnDiffActions, type TurnDiffSource, type TurnPatchOutcome } from '../../hooks/useTurnDiffActions'
import { useConversationStore } from '../../stores/conversationStore'
import { showToast, type ToastType } from '../../stores/toastStore'
import { useShallow } from 'zustand/react/shallow'
import { filesForTurns, turnPatchTotals } from '../../stores/turnDiffs'
import { useUIStore } from '../../stores/uiStore'
import type { TurnFileChange } from '../../types/turnDiff'
import { basename } from '../../utils/path'
import { ChangePath } from '../detail/changes/ChangePath'
import { Button } from '../ui/Button'
import { DisclosureChevron } from '../ui/DisclosureChevron'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { FileDiffStats } from './FileDiffStats'
import { InlineDiffView } from './InlineDiffView'
import { groupTurnFileChanges, type TurnFileGroup } from './turnFileGroups'
import styles from './TurnCompletionSummary.module.css'

const SquarePlusMinusIcon = createLucideIcon('square-plus-minus', [
  ['rect', { x: '4', y: '4', width: '16', height: '16', rx: '4', key: 'frame' }],
  ['path', { d: 'M12 7.5v5', key: 'plus-v' }],
  ['path', { d: 'M9.5 10h5', key: 'plus-h' }],
  ['path', { d: 'M9.5 16h5', key: 'minus' }]
])

const COLLAPSED_ROW_LIMIT = 3

export const TurnCompletionSummary = memo(function TurnCompletionSummary({ turnIds, threadId }: {
  turnIds: readonly string[]
  threadId: string
}): JSX.Element | null {
  const rows = useConversationStore(useShallow((s) => filesForTurns(s.turnDiffs, turnIds)))
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const review = (key: string): void => useUIStore.getState().showChangesForKey(key, { threadId, turnIds })
  return <TurnChangesCard turnIds={turnIds} rows={rows} workspacePath={workspacePath} onReview={review} />
})

interface TurnChangesCardProps {
  turnIds: readonly string[]
  rows: TurnFileChange[]
  workspacePath: string
  source?: TurnDiffSource
  onReview?: (key: string) => void
}

export function TurnChangesCard({ turnIds, rows, workspacePath, source, onReview }: TurnChangesCardProps): JSX.Element | null {
  const t = useT()
  const files = useMemo(() => groupTurnFileChanges(rows, workspacePath), [rows, workspacePath])
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

  const title = files.length === 1
    ? t('turnChanges.editedFile', { file: basename(files[0].filePath) })
    : t('turnChanges.editedFiles', { count: files.length })
  const visibleFiles = showAll ? files : files.slice(0, COLLAPSED_ROW_LIMIT)
  const hiddenCount = files.length - visibleFiles.length

  return (
    <section className={styles.card} aria-label={title}>
      <div className={styles.header}>
        <span className={styles.iconTile} aria-hidden>
          <SquarePlusMinusIcon size={20} strokeWidth={1.5} />
        </span>
        <div className={styles.heading}>
          <span className={styles.title}>{title}</span>
          <span className={styles.stats}>
            <FileDiffStats additions={totals.additions} deletions={totals.deletions} />
          </span>
        </div>
        <div className={styles.actions}>
          <TurnPatchButton turnIds={turnIds} rows={rows} workspacePath={workspacePath} source={source} />
          {onReview && (
            <Button variant="outlineGhost" size="toolbar" onClick={() => onReview(rows[0].key)}>
              {t('turnChanges.review')}
            </Button>
          )}
        </div>
      </div>

      {files.length > 1 && (
        <>
          <div role="list">
            {visibleFiles.map((file) => (
              <TurnFileRow
                key={file.filePath}
                file={file}
                expanded={expanded.has(file.filePath)}
                onToggle={() => toggleRow(file.filePath)}
                onReview={onReview}
              />
            ))}
          </div>
          {files.length > COLLAPSED_ROW_LIMIT && (
            <button
              type="button"
              className={styles.moreToggle}
              aria-expanded={showAll}
              onClick={() => setShowAll((current) => !current)}
            >
              {showAll
                ? t('turnChanges.collapseFiles')
                : t(hiddenCount === 1 ? 'turnChanges.showMoreFiles.one' : 'turnChanges.showMoreFiles.other', { count: hiddenCount })}
              <DisclosureChevron expanded={showAll} direction="reveal" />
            </button>
          )}
        </>
      )}
    </section>
  )
}

function outcomeToast(outcome: Exclude<TurnPatchOutcome, 'not-git-repo'>, undo: boolean): { key: MessageKey; type: ToastType } {
  switch (outcome) {
    case 'reverted': return { key: 'turnChanges.toast.reverted', type: 'success' }
    case 'reapplied': return { key: 'turnChanges.toast.reapplied', type: 'success' }
    case 'partial': return { key: undo ? 'turnChanges.toast.revertPartial' : 'turnChanges.toast.reapplyPartial', type: 'warning' }
    case 'failed': return { key: undo ? 'turnChanges.toast.revertFailed' : 'turnChanges.toast.reapplyFailed', type: 'error' }
  }
}

function TurnPatchButton({
  turnIds,
  rows,
  workspacePath,
  source
}: {
  turnIds: readonly string[]
  rows: TurnFileChange[]
  workspacePath: string
  source?: TurnDiffSource
}): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const { revert, reapply } = useTurnDiffActions(workspacePath, source)
  const [pending, setPending] = useState(false)
  const applicable = rows.filter((row) => !row.truncated)
  const undo = applicable.some((row) => row.diff.status === 'written')

  async function handleClick(): Promise<void> {
    setPending(true)
    const outcome = await (undo ? revert(turnIds) : reapply(turnIds))
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
      size="toolbar"
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
  file,
  expanded,
  onToggle,
  onReview
}: {
  file: TurnFileGroup
  expanded: boolean
  onToggle: () => void
  onReview?: (key: string) => void
}): JSX.Element {
  const t = useT()

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
    if (event.key !== 'Enter' && event.key !== ' ') return
    event.preventDefault()
    onToggle()
  }

  function review(event: MouseEvent<HTMLButtonElement>): void {
    event.stopPropagation()
    onReview?.(file.rows[0].key)
  }
  const path = <ChangePath path={file.filePath} />

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
          {onReview
            ? <button type="button" className={styles.pathLink} onClick={review}>{path}</button>
            : path}
        </span>
        <span className={styles.stats}>
          <FileDiffStats
            additions={file.additions}
            deletions={file.deletions}
            tone={file.rows.every((row) => row.diff.status === 'reverted') ? 'dimmed' : 'semantic'}
          />
        </span>
        <span className={styles.chevron}>
          <DisclosureChevron expanded={expanded} />
        </span>
      </div>
      {expanded && (
        <div className={styles.diff}>
          {file.rows.some((row) => row.truncated) && <p className={styles.truncatedNote}>{t('changes.truncated')}</p>}
          {file.rows.filter((row) => !row.truncated).map((row) => (
            <InlineDiffView key={row.key} diff={row.diff} variant="embedded" presentation="body-only" />
          ))}
        </div>
      )}
    </div>
  )
}
