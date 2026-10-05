import { useLayoutEffect, useMemo, useState, type FocusEvent } from 'react'
import { createPortal } from 'react-dom'
import { useT } from '../../contexts/LocaleContext'
import { useTransientOverlay } from '../../hooks/useTransientOverlay'
import { useConversationStore } from '../../stores/conversationStore'
import { runningTurnFiles, turnPatchTotals } from '../../stores/turnDiffs'
import { useUIStore } from '../../stores/uiStore'
import type { TurnFileChange } from '../../types/turnDiff'
import { toWorkspaceRelativePath } from '../../utils/workspacePaths'
import { ChangePath } from '../detail/changes/ChangePath'
import { placeTooltip } from '../ui/ActionTooltip'
import { FileDiffStats } from './FileDiffStats'
import styles from './RunningTurnChangesPill.module.css'

const CLOSE_DELAY_MS = 120
const NO_ROWS: TurnFileChange[] = []

interface ChangedFile {
  key: string
  filePath: string
  additions: number
  deletions: number
}

function changedFiles(rows: readonly TurnFileChange[]): ChangedFile[] {
  const byPath = new Map<string, ChangedFile>()
  for (const row of rows) {
    const path = row.diff.filePath.replace(/\\/g, '/')
    const existing = byPath.get(path)
    byPath.set(path, {
      key: row.key,
      filePath: row.diff.filePath,
      additions: (existing?.additions ?? 0) + row.diff.additions,
      deletions: (existing?.deletions ?? 0) + row.diff.deletions
    })
  }
  return [...byPath.values()]
}

export function RunningTurnChangesPill({ bottomPx }: { bottomPx: number }): JSX.Element | null {
  const t = useT()
  const rows = useConversationStore(runningTurnFiles)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const [shownRows, setShownRows] = useState(rows)
  if (rows.length > 0 && rows !== shownRows) setShownRows(rows)
  const leaving = rows.length === 0
  const files = useMemo(() => changedFiles(shownRows), [shownRows])
  const totals = useMemo(() => turnPatchTotals(shownRows), [shownRows])
  const card = useTransientOverlay<HTMLDivElement, HTMLDivElement>({
    interactive: true,
    disabled: leaving,
    closeDelayMs: CLOSE_DELAY_MS
  })
  const [position, setPosition] = useState({ left: 0, top: 0 })

  useLayoutEffect(() => {
    const anchor = card.anchorRef.current
    const overlay = card.overlayRef.current
    if (!card.visible || !anchor || !overlay) return
    setPosition(placeTooltip(anchor.getBoundingClientRect(), overlay.getBoundingClientRect(), 'top'))
  }, [card.visible, card.anchorRef, card.overlayRef, files])

  if (shownRows.length === 0) return null

  const label = t(totals.files === 1 ? 'turnChanges.filesChanged.one' : 'turnChanges.filesChanged.other', {
    count: totals.files
  })

  function review(key: string): void {
    card.hide()
    useUIStore.getState().showChangesForKey(key)
  }

  function handleBlur(event: FocusEvent<HTMLDivElement>): void {
    if (card.overlayRef.current?.contains(event.relatedTarget as Node | null)) return
    card.scheduleClose()
  }

  return (
    <div className={styles.slot} style={{ bottom: bottomPx }}>
      <div
        ref={card.anchorRef}
        className={styles.strip}
        data-leaving={leaving || undefined}
        onAnimationEnd={(event) => {
          if (leaving && event.target === event.currentTarget) setShownRows(NO_ROWS)
        }}
        onMouseEnter={card.scheduleOpen}
        onMouseLeave={card.scheduleClose}
        onFocus={card.open}
        onBlur={handleBlur}
      >
        <button type="button" className={styles.summary} onClick={() => review(shownRows[0].key)}>
          <span className={styles.label}>{label}</span>
          <FileDiffStats additions={totals.additions} deletions={totals.deletions} />
        </button>
      </div>
      {card.visible && createPortal(
        <div
          ref={card.overlayRef}
          role="dialog"
          aria-label={label}
          className={styles.card}
          style={{ left: position.left, top: position.top }}
          onMouseEnter={card.cancelClose}
          onMouseLeave={card.scheduleClose}
          onFocus={card.cancelClose}
        >
          {files.map((file) => (
            <button key={file.key} type="button" className={styles.file} onClick={() => review(file.key)}>
              <ChangePath path={toWorkspaceRelativePath(workspacePath, file.filePath)} />
              <span className={styles.fileStats}>
                <FileDiffStats additions={file.additions} deletions={file.deletions} />
              </span>
            </button>
          ))}
        </div>,
        document.body
      )}
    </div>
  )
}
