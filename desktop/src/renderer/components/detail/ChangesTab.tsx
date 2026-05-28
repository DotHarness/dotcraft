import { useEffect, useMemo, useRef, useState, type CSSProperties, type KeyboardEvent } from 'react'
import { ChevronDown, ChevronUp, Columns2, FolderOpen, Rows2, Undo2 } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore, type ChangesDiffMode } from '../../stores/uiStore'
import { useFileChangeActions } from '../../hooks/useFileChangeActions'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { ActionTooltip } from '../ui/ActionTooltip'
import type { FileDiff } from '../../types/toolCall'
import { DiffViewer } from './DiffViewer'

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
  const confirm = useConfirmDialog()
  const { revertFileDiff, reapplyFileDiff } = useFileChangeActions(workspacePath)
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set())
  const initialExpansionAppliedRef = useRef(false)
  const appliedSelectedFileRef = useRef<string | null>(null)

  const files = useMemo(() => Array.from(changedFiles.values()), [changedFiles])
  const writtenFiles = files.filter((f) => f.status === 'written')
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

  async function handleRevertAll(): Promise<void> {
    const count = writtenFiles.length
    if (count === 0) return
    const enPlural = locale === 'zh-Hans' ? '' : count === 1 ? '' : 's'
    const confirmed = await confirm({
      title: t('changes.revertAllTitle'),
      message: t('changes.revertAllMessage', {
        count,
        plural: enPlural
      }),
      confirmLabel: t('changes.revertAllConfirm'),
      danger: true
    })
    if (!confirmed) return
    for (const diff of writtenFiles) {
      await handleRevert(diff)
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
        <DiffModeToggle
          mode={mode}
          onChange={(next) => setMode(activeThreadId, next)}
        />
        {writtenFiles.length > 0 && (
          <ActionTooltip label={t('changes.revertAllTitle')} placement="bottom">
            <button
              type="button"
              onClick={handleRevertAll}
              style={ghostButtonStyle}
            >
              <Undo2 size={13} strokeWidth={1.8} aria-hidden />
              <span>{t('changes.revertAllButton')}</span>
            </button>
          </ActionTooltip>
        )}
      </div>

      <div
        style={{
          flex: 1,
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
            expanded={expanded.has(file.filePath)}
            first={index === 0}
            onToggle={() => toggleFile(file.filePath)}
            onRevert={() => { void handleRevert(file) }}
            onReapply={() => { void handleReapply(file) }}
          />
        ))}
      </div>
    </div>
  )
}

interface FileDiffSectionProps {
  file: FileDiff
  workspacePath: string
  mode: ChangesDiffMode
  expanded: boolean
  first: boolean
  onToggle: () => void
  onRevert: () => void
  onReapply: () => void
}

function FileDiffSection({
  file,
  workspacePath,
  mode,
  expanded,
  first,
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
    const target = parentDirectory(resolveAbsolutePath(file.filePath, workspacePath))
    if (!target) return
    try {
      await window.api.shell.launchEditor('explorer', target)
    } catch (err) {
      console.error('Open folder failed:', err)
    }
  }

  return (
    <section
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
          <DiffViewer diff={file} workspacePath={workspacePath} mode={mode} />
        </div>
      )}
    </section>
  )
}

function DiffModeToggle({
  mode,
  onChange
}: {
  mode: ChangesDiffMode
  onChange: (mode: ChangesDiffMode) => void
}): JSX.Element {
  const t = useT()
  return (
    <div style={segmentedStyle}>
      <ActionTooltip label={t('diffViewer.inlineMode')} placement="bottom">
        <button
          type="button"
          aria-label={t('diffViewer.inlineMode')}
          aria-pressed={mode === 'inline'}
          onClick={() => onChange('inline')}
          style={segmentButtonStyle(mode === 'inline', true)}
        >
          <Rows2 size={14} strokeWidth={1.8} aria-hidden />
        </button>
      </ActionTooltip>
      <ActionTooltip label={t('diffViewer.splitMode')} placement="bottom">
        <button
          type="button"
          aria-label={t('diffViewer.splitMode')}
          aria-pressed={mode === 'split'}
          onClick={() => onChange('split')}
          style={segmentButtonStyle(mode === 'split', false)}
        >
          <Columns2 size={14} strokeWidth={1.8} aria-hidden />
        </button>
      </ActionTooltip>
    </div>
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
  return `${workspacePath.replace(/[\\/]$/, '')}${separator}${filePath.replace(/^[\\/]/, '')}`
}

function isAbsolutePath(filePath: string): boolean {
  return /^[A-Za-z]:[\\/]/.test(filePath) || filePath.startsWith('/') || filePath.startsWith('\\\\')
}

function parentDirectory(filePath: string): string {
  const trimmed = filePath.replace(/[\\/]$/, '')
  const idx = Math.max(trimmed.lastIndexOf('/'), trimmed.lastIndexOf('\\'))
  if (idx <= 0) return trimmed
  return trimmed.slice(0, idx)
}

const summaryHeaderStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  minHeight: '34px',
  padding: '4px 8px 4px 10px',
  borderBottom: '1px solid var(--border-default)',
  flexShrink: 0,
  fontSize: '12px',
  color: 'var(--text-secondary)'
}

const ghostButtonStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '5px',
  minHeight: '24px',
  padding: '2px 7px',
  fontSize: '11px',
  borderRadius: '5px',
  border: '1px solid var(--border-default)',
  background: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer'
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

const segmentedStyle: CSSProperties = {
  display: 'inline-flex',
  overflow: 'hidden',
  border: '1px solid var(--border-default)',
  borderRadius: '6px',
  flexShrink: 0
}

function segmentButtonStyle(active: boolean, divider: boolean): CSSProperties {
  return {
    width: '26px',
    height: '24px',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: 0,
    border: 'none',
    borderRight: divider ? '1px solid var(--border-default)' : 'none',
    background: active ? 'var(--bg-tertiary)' : 'transparent',
    color: active ? 'var(--text-primary)' : 'var(--text-secondary)',
    cursor: 'pointer'
  }
}
