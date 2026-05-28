import { memo, useMemo, useState, type MouseEvent } from 'react'
import { ChevronDown, ChevronUp, Undo2 } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useUIStore } from '../../stores/uiStore'
import { useFileChangeActions } from '../../hooks/useFileChangeActions'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import type { FileDiff } from '../../types/toolCall'
import { InlineDiffView } from './InlineDiffView'

interface TurnCompletionSummaryProps {
  turnId: string
}

export const TurnCompletionSummary = memo(function TurnCompletionSummary({ turnId }: TurnCompletionSummaryProps): JSX.Element | null {
  const t = useT()
  const locale = useLocale()
  const changedFiles = useConversationStore((s) => s.changedFiles)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const confirm = useConfirmDialog()
  const { revertFileDiffs } = useFileChangeActions(workspacePath)
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set())

  const turnFiles = useMemo(() => {
    return Array.from(changedFiles.values()).filter((f) => turnIncludesFile(f, turnId))
  }, [changedFiles, turnId])

  if (turnFiles.length === 0) return null

  const writtenFiles = turnFiles.filter((file) => file.status === 'written')
  const totalAdd = turnFiles.reduce((sum, f) => sum + f.additions, 0)
  const totalDel = turnFiles.reduce((sum, f) => sum + f.deletions, 0)
  const hasReverted = writtenFiles.length === 0

  function toggleFile(filePath: string): void {
    setExpanded((current) => {
      const next = new Set(current)
      if (next.has(filePath)) next.delete(filePath)
      else next.add(filePath)
      return next
    })
  }

  async function handleUndo(): Promise<void> {
    if (writtenFiles.length === 0) return
    const confirmed = await confirm({
      title: t('turnChanges.undoTitle'),
      message: t('turnChanges.undoMessage', {
        count: writtenFiles.length,
        plural: locale === 'zh-Hans' ? '' : writtenFiles.length === 1 ? '' : 's'
      }),
      confirmLabel: t('turnChanges.undoConfirm'),
      danger: true
    })
    if (!confirmed) return
    try {
      await revertFileDiffs(writtenFiles)
    } catch (err) {
      console.error('Undo turn changes failed:', err)
    }
  }

  return (
    <div
      style={{
        borderRadius: '8px',
        border: '1px solid var(--border-default)',
        background: 'var(--bg-primary)',
        overflow: 'hidden',
        marginTop: '8px',
        fontSize: '13px'
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          padding: '9px 12px',
          background: 'var(--bg-secondary)',
          color: 'var(--text-primary)'
        }}
      >
        <span style={{ flex: 1 }}>
          {t('turnChanges.summaryLine', {
            count: turnFiles.length,
            plural: locale === 'zh-Hans' ? '' : turnFiles.length === 1 ? '' : 's'
          })}
        </span>
        <span style={{ display: 'inline-flex', gap: '6px', fontFamily: 'var(--font-mono)', fontSize: '12px' }}>
          {totalAdd > 0 && <span style={{ color: 'var(--success)' }}>+{totalAdd}</span>}
          {totalDel > 0 && <span style={{ color: 'var(--error)' }}>-{totalDel}</span>}
        </span>
        <button
          type="button"
          disabled={hasReverted}
          aria-label={hasReverted ? t('turnChanges.reverted') : t('turnChanges.undo')}
          onClick={() => { void handleUndo() }}
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: '5px',
            border: 'none',
            background: 'transparent',
            color: hasReverted ? 'var(--text-dimmed)' : 'var(--text-secondary)',
            cursor: hasReverted ? 'default' : 'pointer',
            padding: '2px 4px',
            fontSize: '12px'
          }}
        >
          {hasReverted ? t('turnChanges.reverted') : t('turnChanges.undo')}
          {!hasReverted && <Undo2 size={14} strokeWidth={1.8} aria-hidden />}
        </button>
      </div>

      {turnFiles.map((file, idx) => {
        const isExpanded = expanded.has(file.filePath)
        return (
          <div key={file.filePath}>
            <div
              role="button"
              tabIndex={0}
              onClick={() => toggleFile(file.filePath)}
              onKeyDown={(event) => {
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault()
                  toggleFile(file.filePath)
                }
              }}
              style={{
                width: '100%',
                minHeight: '42px',
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
                padding: '8px 12px',
                border: 'none',
                borderTop: idx === 0 ? 'none' : '1px solid var(--border-default)',
                background: isExpanded ? 'var(--bg-tertiary)' : 'var(--bg-primary)',
                color: 'var(--text-primary)',
                cursor: 'pointer',
                textAlign: 'left',
                fontSize: '13px'
              }}
            >
              <span style={{ flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                <FilePathLink filePath={file.filePath} />
                {file.isNewFile && (
                  <NewFileDot label={t('diffViewer.newFile')} />
                )}
              </span>
              <FileStats additions={file.additions} deletions={file.deletions} status={file.status} />
              <span style={{ color: 'var(--text-secondary)', width: '16px', display: 'inline-flex', justifyContent: 'center' }}>
                {isExpanded ? <ChevronUp size={15} strokeWidth={1.8} /> : <ChevronDown size={15} strokeWidth={1.8} />}
              </span>
            </div>
            {isExpanded && (
              <div style={{ borderTop: '1px solid var(--border-default)', background: 'var(--bg-primary)' }}>
                <InlineDiffView diff={file} />
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
})

interface FilePathLinkProps {
  filePath: string
}

function FilePathLink({ filePath }: FilePathLinkProps): JSX.Element {
  function handleClick(event: MouseEvent<HTMLButtonElement>): void {
    event.stopPropagation()
    useUIStore.getState().showChangesForFile(filePath)
  }

  return (
    <button
      type="button"
      onClick={handleClick}
      style={{
        background: 'none',
        border: 'none',
        padding: 0,
        color: 'var(--text-primary)',
        cursor: 'pointer',
        fontFamily: 'var(--font-mono)',
        fontSize: '12px',
        textAlign: 'left',
        maxWidth: '100%',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
        verticalAlign: 'bottom'
      }}
    >
      {filePath}
    </button>
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

interface FileStatsProps {
  additions: number
  deletions: number
  status: 'written' | 'reverted'
}

function FileStats({ additions, deletions, status }: FileStatsProps): JSX.Element {
  const dim = status === 'reverted'
  return (
    <span style={{ display: 'inline-flex', gap: '6px', flexShrink: 0, fontFamily: 'var(--font-mono)', fontSize: '12px' }}>
      {additions > 0 && (
        <span style={{ color: dim ? 'var(--text-dimmed)' : 'var(--success)' }}>+{additions}</span>
      )}
      {deletions > 0 && (
        <span style={{ color: dim ? 'var(--text-dimmed)' : 'var(--error)' }}>-{deletions}</span>
      )}
    </span>
  )
}

function turnIncludesFile(file: FileDiff, turnId: string): boolean {
  const ids = file.turnIds?.length ? file.turnIds : [file.turnId]
  return ids.includes(turnId)
}
