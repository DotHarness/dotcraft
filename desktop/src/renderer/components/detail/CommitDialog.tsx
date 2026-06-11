import { useState, useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { ChevronDown, ChevronRight, GitBranch, GitCommitHorizontal } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { ModalHeader } from '../ui/ModalHeader'

interface CommitDialogProps {
  workspacePath: string
  /**
   * Hands the (possibly empty) message off to the owner. The owner runs the
   * async commit + toast lifecycle so it survives this dialog unmounting; the
   * dialog never sits in a committing/generating state. Spec §16.5.
   */
  onCommit: (message: string) => void
  onClose: () => void
}

/**
 * Frameless modal for staging and committing written file changes to git.
 * Collects an optional commit message (blank = autogenerate) and the branch /
 * change summary, then hands off to the owner and closes. Spec §16.5.
 */
export function CommitDialog({ workspacePath, onCommit, onClose }: CommitDialogProps): JSX.Element {
  const t = useT()
  const changedFiles = useConversationStore((s) => s.changedFiles)

  const allFiles = Array.from(changedFiles.values())
  const writtenFiles = allFiles.filter((f) => f.status === 'written')
  const revertedCount = allFiles.length - writtenFiles.length

  const [message, setMessage] = useState('')
  const [branch, setBranch] = useState<string | null>(null)
  const [filesExpanded, setFilesExpanded] = useState(false)
  const messageRef = useRef<HTMLTextAreaElement>(null)
  const totalAdditions = writtenFiles.reduce((sum, file) => sum + file.additions, 0)
  const totalDeletions = writtenFiles.reduce((sum, file) => sum + file.deletions, 0)
  const hasFiles = writtenFiles.length > 0

  useEffect(() => {
    messageRef.current?.focus()

    function handleKeyDown(e: KeyboardEvent): void {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  useEffect(() => {
    let cancelled = false
    if (!workspacePath.trim()) {
      setBranch(null)
      return
    }
    void window.api.git
      .getBranch(workspacePath)
      .then((value) => {
        if (!cancelled) setBranch(value)
      })
      .catch(() => {
        if (!cancelled) setBranch(null)
      })
    return () => {
      cancelled = true
    }
  }, [workspacePath])

  function handleSubmit(): void {
    if (!hasFiles) return
    // Hand off and leave immediately — the owner runs the commit via toasts.
    onCommit(message.trim())
    onClose()
  }

  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="commit-title"
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
          icon={<GitCommitHorizontal size={18} aria-hidden="true" />}
          title={t('commit.title')}
          titleId="commit-title"
          onClose={onClose}
          closeLabel={t('commit.close')}
        />

        {/* Frameless info rows */}
        <div style={infoRowStyle}>
          <span style={infoLabelStyle}>{t('commit.branchLabel')}</span>
          <span style={infoValueStyle}>
            <GitBranch size={14} />
            {branch || t('commit.detachedHead')}
          </span>
        </div>

        <button
          type="button"
          onClick={() => setFilesExpanded((v) => !v)}
          aria-label={filesExpanded ? t('commit.collapseFiles') : t('commit.expandFiles')}
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
          <span style={infoLabelStyle}>{t('commit.changesLabel')}</span>
          <span style={{ ...infoValueStyle, gap: '8px' }}>
            <span>{t('commit.changesSummary', { files: writtenFiles.length })}</span>
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
              {t('commit.filesHeader', {
                written: writtenFiles.length,
                all: allFiles.length,
                reverted:
                  revertedCount > 0 ? t('commit.revertedSuffix', { count: revertedCount }) : ''
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
          {t('commit.messageLabel')}
        </div>

        <textarea
          ref={messageRef}
          className="dc-dialog-input"
          value={message}
          onChange={(e) => setMessage(e.target.value)}
          onKeyDown={(e) => {
            if ((e.metaKey || e.ctrlKey) && e.key === 'Enter') {
              e.preventDefault()
              handleSubmit()
            }
          }}
          rows={3}
          placeholder={t('commit.placeholderAuto')}
        />

        <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '16px' }}>
          <button
            type="button"
            onClick={handleSubmit}
            disabled={!hasFiles}
            style={{
              padding: '8px 18px',
              border: '1px solid var(--text-primary)',
              borderRadius: '9px',
              backgroundColor: 'var(--text-primary)',
              color: 'var(--bg-primary)',
              fontSize: '13px',
              fontWeight: 600,
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'center',
              gap: '7px',
              cursor: hasFiles ? 'pointer' : 'default',
              opacity: hasFiles ? 1 : 0.5
            }}
          >
            <GitCommitHorizontal size={16} />
            {t('commit.button')}
          </button>
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

export function toRelativePath(filePath: string, workspacePath: string): string {
  if (!workspacePath) return filePath
  const ws = workspacePath.replace(/\\/g, '/').replace(/\/$/, '')
  const fp = filePath.replace(/\\/g, '/')
  if (fp.startsWith(ws + '/')) return fp.slice(ws.length + 1)
  return filePath
}
