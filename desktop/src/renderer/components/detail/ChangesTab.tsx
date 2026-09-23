import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { Columns2, Folder, FolderOpen, Rows2 } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { latestTurnDiff, turnPatchTotals } from '../../stores/turnDiffs'
import { useUIStore, type ChangesDiffMode } from '../../stores/uiStore'
import type { TurnFileChange } from '../../types/turnDiff'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ChangesActionsMenu } from './ChangesActionsMenu'
import { ChangesFileList } from './ChangesFileList'
import { JumpToFileButton } from './JumpToFileButton'
import { ExplorerDock } from './ExplorerDock'
import { FileDiffSection, FileStats } from './changes/FileDiffSection'

interface ChangesTabProps {
  workspacePath: string
}

const NO_ROWS: TurnFileChange[] = []

export function ChangesTab({ workspacePath }: ChangesTabProps): JSX.Element {
  const t = useT()
  const rows = useConversationStore((s) => latestTurnDiff(s.turnDiffs, s.turns)?.files ?? NO_ROWS)
  const totals = useMemo(() => turnPatchTotals(rows), [rows])
  const selectedKey = useUIStore((s) => s.selectedChangeKey)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const mode = useUIStore((s) => s.getChangesDiffMode(activeThreadId))
  const setMode = useUIStore((s) => s.setChangesDiffMode)
  const wordWrap = useUIStore((s) => s.changesWordWrap)
  const toggleWordWrap = useUIStore((s) => s.toggleChangesWordWrap)
  const explorerVisible = useUIStore((s) => s.explorerVisible)
  const toggleExplorer = useUIStore((s) => s.toggleExplorer)
  const explorerWidth = useUIStore((s) => s.explorerWidth)
  const selectChangeKey = useUIStore((s) => s.selectChangeKey)
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set())
  const initialExpansionAppliedRef = useRef(false)
  const appliedSelectedKeyRef = useRef<string | null>(null)
  const sectionRefs = useRef<Map<string, HTMLElement>>(new Map())

  const registerSection = useCallback((key: string, node: HTMLElement | null) => {
    if (node) sectionRefs.current.set(key, node)
    else sectionRefs.current.delete(key)
  }, [])

  const handleExplorerDrag = useCallback((delta: number) => {
    const state = useUIStore.getState()
    state.setExplorerWidth(state.explorerWidth - delta)
  }, [])

  useEffect(() => {
    initialExpansionAppliedRef.current = false
    appliedSelectedKeyRef.current = null
    setExpanded(new Set())
  }, [activeThreadId])

  useEffect(() => {
    if (rows.length === 0) {
      initialExpansionAppliedRef.current = false
      appliedSelectedKeyRef.current = null
      setExpanded((current) => current.size === 0 ? current : new Set())
      return
    }

    setExpanded((current) => {
      const available = new Set(rows.map((row) => row.key))
      const next = new Set([...current].filter((key) => available.has(key)))
      if (selectedKey && available.has(selectedKey) && appliedSelectedKeyRef.current !== selectedKey) {
        next.add(selectedKey)
        appliedSelectedKeyRef.current = selectedKey
      } else if (!initialExpansionAppliedRef.current) {
        const firstKey = rows[0]?.key
        if (firstKey) next.add(firstKey)
      }
      initialExpansionAppliedRef.current = true
      return next
    })
  }, [rows, selectedKey])

  function toggleRow(key: string): void {
    setExpanded((current) => {
      const next = new Set(current)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
  }

  function expandAll(): void {
    setExpanded(new Set(rows.map((row) => row.key)))
  }

  function collapseAll(): void {
    setExpanded(new Set())
  }

  // Explorer row click: expand the file, mark it selected, and scroll its diff
  // section into view (re-scrolls on every click, even if already expanded).
  function handleSelectFromExplorer(key: string): void {
    setExpanded((current) => current.has(key) ? current : new Set(current).add(key))
    selectChangeKey(key)
    requestAnimationFrame(() => {
      sectionRefs.current.get(key)?.scrollIntoView({ block: 'start' })
    })
  }

  if (rows.length === 0) {
    return (
      <div
        style={{
          flex: 1,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          padding: '16px'
        }}
      >
        <p
          style={{
            textAlign: 'center',
            color: 'var(--text-dimmed)',
            fontSize: '13px',
            lineHeight: 1.7,
            whiteSpace: 'pre-line'
          }}
        >
          {t('changes.empty')}
        </p>
      </div>
    )
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%', overflow: 'hidden' }}>
      <div style={summaryHeaderStyle}>
        <span>{t('changes.lastTurn')}</span>
        <FileStats additions={totals.additions} deletions={totals.deletions} />
        <span style={{ flex: 1 }} />
        <div style={actionsClusterStyle}>
          <ChangesActionsMenu
            wordWrap={wordWrap}
            onToggleWordWrap={toggleWordWrap}
            onExpandAll={expandAll}
            onCollapseAll={collapseAll}
          />
          <JumpToFileButton />
          <DiffModeToggle
            mode={mode}
            onChange={(next) => setMode(activeThreadId, next)}
          />
          <ActionTooltip
            label={explorerVisible ? t('viewer.closeExplorer') : t('viewer.openExplorer')}
            placement="bottom"
          >
            <button
              type="button"
              aria-label={explorerVisible ? t('viewer.closeExplorer') : t('viewer.openExplorer')}
              aria-pressed={explorerVisible}
              onClick={toggleExplorer}
              style={{
                ...headerIconButtonStyle,
                color: explorerVisible ? 'var(--text-primary)' : 'var(--text-secondary)',
                background: explorerVisible ? 'var(--bg-tertiary)' : 'transparent'
              }}
              onMouseEnter={(e) => { (e.currentTarget as HTMLButtonElement).style.background = 'var(--bg-tertiary)' }}
              onMouseLeave={(e) => { (e.currentTarget as HTMLButtonElement).style.background = explorerVisible ? 'var(--bg-tertiary)' : 'transparent' }}
            >
              {explorerVisible
                ? <FolderOpen size={16} aria-hidden style={{ display: 'block' }} />
                : <Folder size={16} aria-hidden style={{ display: 'block' }} />}
            </button>
          </ActionTooltip>
        </div>
      </div>

      <div style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'row' }}>
        <div
          className="dc-scrollbar-stable"
          style={{
            flex: '1 1 0',
            minWidth: 160,
            overflow: 'auto',
            padding: '4px 0 12px'
          }}
        >
          {rows.map((row) => (
            <FileDiffSection
              key={row.key}
              row={row}
              workspacePath={workspacePath}
              mode={mode}
              wordWrap={wordWrap}
              expanded={expanded.has(row.key)}
              registerSection={registerSection}
              onToggle={() => toggleRow(row.key)}
            />
          ))}
        </div>

        {explorerVisible && (
          <ExplorerDock width={explorerWidth} onDrag={handleExplorerDrag}>
            <ChangesFileList
              changes={rows}
              workspacePath={workspacePath}
              selectedKey={selectedKey}
              onSelect={handleSelectFromExplorer}
            />
          </ExplorerDock>
        )}
      </div>
    </div>
  )
}

/**
 * A single borderless button that toggles between unified and split diff. The
 * icon advertises the *other* mode (what a click switches to), matching its
 * tooltip — so the control reads as one action, not a two-state segmented pair.
 */
function DiffModeToggle({
  mode,
  onChange
}: {
  mode: ChangesDiffMode
  onChange: (mode: ChangesDiffMode) => void
}): JSX.Element {
  const t = useT()
  const next: ChangesDiffMode = mode === 'inline' ? 'split' : 'inline'
  const label = next === 'split' ? t('diffViewer.splitMode') : t('diffViewer.inlineMode')
  return (
    <ActionTooltip label={label} placement="bottom">
      <button
        type="button"
        aria-label={label}
        onClick={() => onChange(next)}
        style={headerIconButtonStyle}
        onMouseEnter={(e) => { (e.currentTarget as HTMLButtonElement).style.background = 'var(--bg-tertiary)' }}
        onMouseLeave={(e) => { (e.currentTarget as HTMLButtonElement).style.background = 'transparent' }}
      >
        {next === 'split'
          ? <Columns2 size={16} strokeWidth={1.8} aria-hidden style={{ display: 'block' }} />
          : <Rows2 size={16} strokeWidth={1.8} aria-hidden style={{ display: 'block' }} />}
      </button>
    </ActionTooltip>
  )
}

const summaryHeaderStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  height: '38px',
  boxSizing: 'border-box',
  padding: '0 8px 0 12px',
  borderBottom: '1px solid var(--glass-border)',
  flexShrink: 0,
  fontSize: '12px',
  color: 'var(--text-secondary)'
}

const actionsClusterStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '4px',
  flexShrink: 0
}

/** Borderless 28×28 header action button, matching `ViewerHeader`. */
const headerIconButtonStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  width: '28px',
  height: '28px',
  padding: 0,
  border: 'none',
  borderRadius: '6px',
  background: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  flexShrink: 0,
  transition: 'background-color 100ms ease, color 100ms ease'
}
