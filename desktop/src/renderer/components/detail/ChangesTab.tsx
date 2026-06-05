import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type KeyboardEvent } from 'react'
import { ChevronDown, ChevronUp, Columns2, Folder, FolderOpen, Rows2, Undo2 } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore, type ChangesDiffMode } from '../../stores/uiStore'
import { useFileChangeActions } from '../../hooks/useFileChangeActions'
import { ActionTooltip } from '../ui/ActionTooltip'
import type { FileDiff } from '../../types/toolCall'
import { DiffViewer } from './DiffViewer'
import { ChangesActionsMenu } from './ChangesActionsMenu'
import { ChangesFileList } from './ChangesFileList'
import { JumpToFileButton } from './JumpToFileButton'
import { DragHandle } from '../layout/DragHandle'

interface ChangesTabProps {
  workspacePath: string
}

/**
 * Changes tab content — a single scroll stream of collapsible file diffs.
 * Handles revert/re-apply by writing files to disk via IPC.
 * Spec §11.3
 */
export function ChangesTab({ workspacePath }: ChangesTabProps): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const changedFiles = useConversationStore((s) => s.changedFiles)
  const selectedFile = useUIStore((s) => s.selectedChangedFile)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const mode = useUIStore((s) => s.getChangesDiffMode(activeThreadId))
  const setMode = useUIStore((s) => s.setChangesDiffMode)
  const wordWrap = useUIStore((s) => s.changesWordWrap)
  const toggleWordWrap = useUIStore((s) => s.toggleChangesWordWrap)
  const explorerVisible = useUIStore((s) => s.explorerVisible)
  const toggleExplorer = useUIStore((s) => s.toggleExplorer)
  const explorerWidth = useUIStore((s) => s.explorerWidth)
  const selectChangedFile = useUIStore((s) => s.selectChangedFile)
  const { revertFileDiff, reapplyFileDiff } = useFileChangeActions(workspacePath)
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set())
  const initialExpansionAppliedRef = useRef(false)
  const appliedSelectedFileRef = useRef<string | null>(null)
  const sectionRefs = useRef<Map<string, HTMLElement>>(new Map())

  const registerSection = useCallback((filePath: string, node: HTMLElement | null) => {
    if (node) sectionRefs.current.set(filePath, node)
    else sectionRefs.current.delete(filePath)
  }, [])

  const handleExplorerDrag = useCallback((delta: number) => {
    const state = useUIStore.getState()
    state.setExplorerWidth(state.explorerWidth - delta)
  }, [])

  const files = useMemo(() => Array.from(changedFiles.values()), [changedFiles])
  const totalAdd = files.reduce((s, f) => s + f.additions, 0)
  const totalDel = files.reduce((s, f) => s + f.deletions, 0)

  useEffect(() => {
    initialExpansionAppliedRef.current = false
    appliedSelectedFileRef.current = null
    setExpanded(new Set())
  }, [activeThreadId])

  useEffect(() => {
    if (files.length === 0) {
      initialExpansionAppliedRef.current = false
      appliedSelectedFileRef.current = null
      setExpanded((current) => current.size === 0 ? current : new Set())
      return
    }

    setExpanded((current) => {
      const available = new Set(files.map((file) => file.filePath))
      const next = new Set([...current].filter((filePath) => available.has(filePath)))
      if (selectedFile && available.has(selectedFile) && appliedSelectedFileRef.current !== selectedFile) {
        next.add(selectedFile)
        appliedSelectedFileRef.current = selectedFile
      } else if (!initialExpansionAppliedRef.current) {
        const firstFile = files[0]?.filePath
        if (firstFile) next.add(firstFile)
      }
      initialExpansionAppliedRef.current = true
      return next
    })
  }, [files, selectedFile])

  async function handleRevert(diff: FileDiff): Promise<void> {
    try {
      await revertFileDiff(diff)
    } catch (err) {
      console.error('Revert failed:', err)
    }
  }

  async function handleReapply(diff: FileDiff): Promise<void> {
    try {
      await reapplyFileDiff(diff)
    } catch (err) {
      console.error('Re-apply failed:', err)
    }
  }

  function toggleFile(filePath: string): void {
    setExpanded((current) => {
      const next = new Set(current)
      if (next.has(filePath)) next.delete(filePath)
      else next.add(filePath)
      return next
    })
  }

  function expandAll(): void {
    setExpanded(new Set(files.map((file) => file.filePath)))
  }

  function collapseAll(): void {
    setExpanded(new Set())
  }

  // Explorer row click: expand the file, mark it selected, and scroll its diff
  // section into view (re-scrolls on every click, even if already expanded).
  function handleSelectFromExplorer(filePath: string): void {
    setExpanded((current) => current.has(filePath) ? current : new Set(current).add(filePath))
    selectChangedFile(filePath)
    requestAnimationFrame(() => {
      sectionRefs.current.get(filePath)?.scrollIntoView({ block: 'start' })
    })
  }

  if (files.length === 0) {
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
        <span>
          {t('changes.summaryLine', {
            count: files.length,
            plural: locale === 'zh-Hans' ? '' : files.length === 1 ? '' : 's'
          })}
        </span>
        <FileStats additions={totalAdd} deletions={totalDel} />
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
          style={{
            flex: '1 1 0',
            minWidth: 160,
            overflow: 'auto',
            padding: '4px 0 12px'
          }}
        >
          {files.map((file, index) => (
            <FileDiffSection
              key={file.filePath}
              file={file}
              workspacePath={workspacePath}
              mode={mode}
              wordWrap={wordWrap}
              expanded={expanded.has(file.filePath)}
              first={index === 0}
              registerSection={registerSection}
              onToggle={() => toggleFile(file.filePath)}
              onRevert={() => { void handleRevert(file) }}
              onReapply={() => { void handleReapply(file) }}
            />
          ))}
        </div>

        {explorerVisible && (
          <>
            <DragHandle onDrag={handleExplorerDrag} />
            <div
              style={{
                flex: `0 1 ${explorerWidth}px`,
                minWidth: 140,
                overflow: 'hidden',
                display: 'flex',
                flexDirection: 'column',
                borderLeft: '1px solid var(--glass-border)'
              }}
            >
              <ChangesFileList
                files={files}
                workspacePath={workspacePath}
                selectedPath={selectedFile}
                onSelect={handleSelectFromExplorer}
              />
            </div>
          </>
        )}
      </div>
    </div>
  )
}

interface FileDiffSectionProps {
  file: FileDiff
  workspacePath: string
  mode: ChangesDiffMode
  wordWrap: boolean
  expanded: boolean
  first: boolean
  registerSection: (filePath: string, node: HTMLElement | null) => void
  onToggle: () => void
  onRevert: () => void
  onReapply: () => void
}

function FileDiffSection({
  file,
  workspacePath,
  mode,
  wordWrap,
  expanded,
  first,
  registerSection,
  onToggle,
  onRevert,
  onReapply
}: FileDiffSectionProps): JSX.Element {
  const t = useT()
  const [active, setActive] = useState(false)
  const isReverted = file.status === 'reverted'
  const relativePath = toRelativePath(file.filePath, workspacePath)

  function handleHeaderKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
    if (event.key !== 'Enter' && event.key !== ' ') return
    event.preventDefault()
    onToggle()
  }

  async function openParentFolder(): Promise<void> {
    const target = resolveAbsolutePath(file.filePath, workspacePath)
    if (!target) return
    try {
      await window.api.shell.showItemInFolder(target)
    } catch (err) {
      console.error('Open folder failed:', err)
    }
  }

  return (
    <section
      ref={(node) => registerSection(file.filePath, node)}
      style={{
        borderTop: first ? 'none' : '1px solid var(--border-default)'
      }}
      onMouseEnter={() => setActive(true)}
      onMouseLeave={() => setActive(false)}
      onFocusCapture={() => setActive(true)}
      onBlurCapture={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
          setActive(false)
        }
      }}
    >
      <div
        role="button"
        tabIndex={0}
        aria-expanded={expanded}
        onClick={onToggle}
        onKeyDown={handleHeaderKeyDown}
        title={relativePath}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          minHeight: '34px',
          padding: '5px 10px',
          color: isReverted ? 'var(--text-dimmed)' : 'var(--text-primary)',
          background: expanded ? 'var(--bg-primary)' : 'transparent',
          cursor: 'pointer',
          userSelect: 'none'
        }}
      >
        <span
          style={{
            minWidth: 0,
            flex: 1,
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
            fontFamily: 'var(--font-mono)',
            fontSize: '12px'
          }}
        >
          {relativePath}
          {file.isNewFile && (
            <NewFileDot label={t('diffViewer.newFile')} />
          )}
          {isReverted && (
            <span style={{ color: 'var(--text-dimmed)', marginLeft: '6px', fontSize: '10px' }}>
              {t('changesFile.reverted')}
            </span>
          )}
        </span>
        <FileStats additions={file.additions} deletions={file.deletions} dim={isReverted} />
        <ActionTooltip label={t('changesFile.openFolder')} placement="bottom">
          <button
            type="button"
            aria-label={t('changesFile.openFolder')}
            onClick={(event) => {
              event.stopPropagation()
              void openParentFolder()
            }}
            style={{
              ...iconButtonStyle,
              opacity: active ? 1 : 0
            }}
          >
            <FolderOpen size={14} strokeWidth={1.8} aria-hidden />
          </button>
        </ActionTooltip>
        <ActionTooltip label={isReverted ? t('changesFile.reapplyTitle') : t('changesFile.revertTitle')} placement="bottom">
          <button
            type="button"
            aria-label={isReverted ? t('changesFile.reapplyTitle') : t('changesFile.revertTitle')}
            onClick={(event) => {
              event.stopPropagation()
              if (isReverted) onReapply()
              else onRevert()
            }}
            style={{
              ...iconButtonStyle,
              opacity: active ? 1 : 0
            }}
          >
            <Undo2 size={14} strokeWidth={1.8} aria-hidden />
          </button>
        </ActionTooltip>
        <span style={{ color: 'var(--text-secondary)', width: '16px', display: 'inline-flex', justifyContent: 'center' }}>
          {expanded ? <ChevronUp size={15} strokeWidth={1.8} /> : <ChevronDown size={15} strokeWidth={1.8} />}
        </span>
      </div>
      {expanded && (
        <div style={{ background: 'var(--bg-primary)' }}>
          <DiffViewer diff={file} workspacePath={workspacePath} mode={mode} wordWrap={wordWrap} />
        </div>
      )}
    </section>
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

function FileStats({
  additions,
  deletions,
  dim = false
}: {
  additions: number
  deletions: number
  dim?: boolean
}): JSX.Element {
  return (
    <span style={{ display: 'inline-flex', gap: '6px', flexShrink: 0, fontFamily: 'var(--font-mono)', fontSize: '12px' }}>
      {additions > 0 && <span style={{ color: dim ? 'var(--text-dimmed)' : 'var(--success)' }}>+{additions}</span>}
      {deletions > 0 && <span style={{ color: dim ? 'var(--text-dimmed)' : 'var(--error)' }}>-{deletions}</span>}
    </span>
  )
}

function NewFileDot({ label }: { label: string }): JSX.Element {
  return (
    <span
      role="img"
      aria-label={label}
      title={label}
      style={newFileDotStyle}
    />
  )
}

const newFileDotStyle: CSSProperties = {
  display: 'inline-block',
  width: '7px',
  height: '7px',
  marginLeft: '6px',
  borderRadius: '999px',
  background: 'var(--success)',
  verticalAlign: 'middle'
}

function toRelativePath(filePath: string, workspacePath: string): string {
  if (!workspacePath) return filePath
  const ws = workspacePath.replace(/\\/g, '/').replace(/\/$/, '')
  const fp = filePath.replace(/\\/g, '/')
  if (fp.startsWith(ws + '/')) return fp.slice(ws.length + 1)
  return filePath
}

function resolveAbsolutePath(filePath: string, workspacePath: string): string {
  if (isAbsolutePath(filePath) || !workspacePath) return filePath
  const separator = workspacePath.includes('\\') ? '\\' : '/'
  const rel = filePath.replace(/^[\\/]/, '').replace(/[\\/]/g, separator)
  return `${workspacePath.replace(/[\\/]$/, '')}${separator}${rel}`
}

function isAbsolutePath(filePath: string): boolean {
  return /^[A-Za-z]:[\\/]/.test(filePath) || filePath.startsWith('/') || filePath.startsWith('\\\\')
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

const iconButtonStyle: CSSProperties = {
  width: '24px',
  height: '24px',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 0,
  borderRadius: '5px',
  border: 'none',
  background: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  flexShrink: 0,
  transition: 'opacity 100ms ease, background-color 100ms ease, color 100ms ease'
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
