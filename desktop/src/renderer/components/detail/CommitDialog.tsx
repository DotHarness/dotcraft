import { useState, useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { ChevronDown, ChevronRight, GitBranch, GitCommit, Loader2, Sparkles, X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { addToast } from '../../stores/toastStore'
import { ActionTooltip } from '../ui/ActionTooltip'

interface CommitDialogProps {
  workspacePath: string
  threadId: string
  onClose: () => void
}

/**
 * Modal dialog for staging and committing file changes to git.
 * Lists only non-reverted (written) files; commit message is pre-populated.
 * Spec §16.5.
 */
export function CommitDialog({ workspacePath, threadId, onClose }: CommitDialogProps): JSX.Element {
  const t = useT()
  const changedFiles = useConversationStore((s) => s.changedFiles)
  const turns = useConversationStore((s) => s.turns)
  const connectionStatus = useConnectionStore((s) => s.status)
  const isConnected = connectionStatus === 'connected'

  const allFiles = Array.from(changedFiles.values())
  const writtenFiles = allFiles.filter((f) => f.status === 'written')
  const revertedCount = allFiles.length - writtenFiles.length

  const [message, setMessage] = useState(() => generateCommitMessage(turns))
  const [committing, setCommitting] = useState(false)
  const [generating, setGenerating] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState(false)
  const [branch, setBranch] = useState<string | null>(null)
  const [filesExpanded, setFilesExpanded] = useState(false)
  const messageRef = useRef<HTMLTextAreaElement>(null)
  const totalAdditions = writtenFiles.reduce((sum, file) => sum + file.additions, 0)
  const totalDeletions = writtenFiles.reduce((sum, file) => sum + file.deletions, 0)

  useEffect(() => {
    messageRef.current?.focus()

    function handleKeyDown(e: KeyboardEvent): void {
      if (e.key === 'Escape' && !committing && !generating) onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [committing, generating, onClose])

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

  async function suggestFromChanges(options?: { applyToEditor?: boolean; showToastOnSuccess?: boolean }): Promise<string | null> {
    if (!isConnected || writtenFiles.length === 0 || !threadId.trim()) return null
    const applyToEditor = options?.applyToEditor ?? true
    const showToastOnSuccess = options?.showToastOnSuccess ?? true
    setGenerating(true)
    setError(null)
    try {
      const paths = writtenFiles.map((f) => toRelativePath(f.filePath, workspacePath))
      const result = (await window.api.appServer.sendRequest(
        'workspace/commitMessage/suggest',
        {
          threadId,
          paths
        },
        120_000
      )) as { message?: string }
      if (result?.message?.trim()) {
        const nextMessage = result.message.trim()
        if (applyToEditor) setMessage(nextMessage)
        if (showToastOnSuccess) addToast(t('commit.toast.generated'), 'success')
        return nextMessage
      } else {
        setError(t('commit.error.emptyServer'))
      }
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err)
      setError(msg)
      addToast(t('commit.toast.generateFailed', { error: msg }), 'error')
      return null
    } finally {
      setGenerating(false)
    }
    return null
  }

  async function handleSuggestFromChanges(): Promise<void> {
    await suggestFromChanges({ applyToEditor: true, showToastOnSuccess: true })
  }

  async function handleSubmit(): Promise<void> {
    if (writtenFiles.length === 0 || committing || generating || success) return
    setCommitting(true)
    setError(null)
    try {
      let commitMessage = message.trim()
      if (!commitMessage) {
        addToast(t('commit.autoGeneratingBeforeCommit'), 'success')
        const generated = await suggestFromChanges({
          applyToEditor: true,
          showToastOnSuccess: false
        })
        if (!generated) {
          setCommitting(false)
          return
        }
        commitMessage = generated
      }
      const filePaths = writtenFiles.map((f) => f.filePath)
      await window.api.git.commit(workspacePath, filePaths, commitMessage)
      setSuccess(true)
      addToast(t('commit.toast.done', { line: commitMessage.split('\n')[0] }), 'success')
      setTimeout(onClose, 1200)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setCommitting(false)
    }
  }

  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={t('commit.title')}
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
        if (e.target === e.currentTarget && !committing && !generating) onClose()
      }}
    >
      <div
        style={{
          backgroundColor: 'var(--bg-secondary)',
          borderRadius: '10px',
          boxShadow: 'var(--shadow-level-3)',
          padding: '24px',
          width: '480px',
          maxWidth: 'calc(100vw - 48px)',
          maxHeight: 'calc(100vh - 96px)',
          overflow: 'auto'
        }}
        onMouseDown={(e) => e.stopPropagation()}
      >
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            marginBottom: '10px'
          }}
        >
          <div
            style={{
              width: '30px',
              height: '30px',
              borderRadius: '8px',
              border: '1px solid var(--border-default)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              color: 'var(--text-primary)'
            }}
          >
            <GitCommit size={16} />
          </div>
          <button
            type="button"
            aria-label={t('commit.close')}
            onClick={onClose}
            disabled={committing || generating}
            style={{
              width: '28px',
              height: '28px',
              borderRadius: '6px',
              border: '1px solid var(--border-default)',
              background: 'transparent',
              color: 'var(--text-secondary)',
              cursor: committing || generating ? 'default' : 'pointer',
              opacity: committing || generating ? 0.5 : 1,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center'
            }}
          >
            <X size={14} />
          </button>
        </div>

        <h2
          style={{
            margin: '0 0 16px',
            fontSize: '17px',
            fontWeight: 600,
            color: 'var(--text-primary)'
          }}
        >
          {t('commit.title')}
        </h2>

        <div
          style={{
            border: '1px solid var(--border-default)',
            borderRadius: '8px',
            marginBottom: '12px'
          }}
        >
          <div
            style={{
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
              padding: '10px 12px',
              borderBottom: '1px solid var(--border-default)',
              fontSize: '12px'
            }}
          >
            <span style={{ color: 'var(--text-secondary)' }}>{t('commit.branchLabel')}</span>
            <span style={{ color: 'var(--text-primary)', display: 'flex', alignItems: 'center', gap: '6px', fontFamily: 'var(--font-mono)' }}>
              <GitBranch size={14} />
              {branch || t('commit.detachedHead')}
            </span>
          </div>

          <button
            type="button"
            onClick={() => setFilesExpanded((v) => !v)}
            aria-label={filesExpanded ? t('commit.collapseFiles') : t('commit.expandFiles')}
            style={{
              width: '100%',
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
              padding: '10px 12px',
              border: 'none',
              borderRadius: 0,
              background: 'transparent',
              fontSize: '12px',
              cursor: 'pointer'
            }}
          >
            <span style={{ color: 'var(--text-secondary)' }}>{t('commit.changesLabel')}</span>
            <span style={{ color: 'var(--text-primary)', display: 'flex', alignItems: 'center', gap: '8px', fontFamily: 'var(--font-mono)' }}>
              <span>{t('commit.changesSummary', { files: writtenFiles.length })}</span>
              <span style={{ color: 'var(--success)' }}>+{totalAdditions}</span>
              <span style={{ color: 'var(--error)' }}>-{totalDeletions}</span>
              {filesExpanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
            </span>
          </button>
        </div>

        {filesExpanded && (
          <>
            <div
              style={{
                fontSize: '12px',
                color: 'var(--text-secondary)',
                marginBottom: '6px'
              }}
            >
              {t('commit.filesHeader', {
                written: writtenFiles.length,
                all: allFiles.length,
                reverted:
                  revertedCount > 0 ? t('commit.revertedSuffix', { count: revertedCount }) : ''
              })}
            </div>

            <div
              style={{
                border: '1px solid var(--border-default)',
                borderRadius: '6px',
                overflow: 'hidden',
                marginBottom: '16px',
                maxHeight: '200px',
                overflowY: 'auto'
              }}
            >
              {writtenFiles.map((file, idx) => (
                <div
                  key={file.filePath}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '8px',
                    padding: '5px 10px',
                    borderBottom: idx < writtenFiles.length - 1 ? '1px solid var(--border-default)' : 'none',
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
                  <span style={{ display: 'flex', gap: '4px', fontFamily: 'var(--font-mono)', fontSize: '11px', flexShrink: 0 }}>
                    {file.additions > 0 && <span style={{ color: 'var(--success)' }}>+{file.additions}</span>}
                    {file.deletions > 0 && <span style={{ color: 'var(--error)' }}>-{file.deletions}</span>}
                  </span>
                </div>
              ))}
            </div>
          </>
        )}

        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: '8px',
            marginBottom: '6px'
          }}
        >
          <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>{t('commit.messageLabel')}</span>
          <ActionTooltip
            label={t('commit.generateTitle.connected')}
            disabledReason={
              !isConnected
                ? t('commit.generateTitle.disconnected')
                : writtenFiles.length === 0
                  ? t('commit.generateTitle.noFiles')
                  : undefined
            }
            placement="top"
          >
            <button
              type="button"
              onClick={() => {
                void handleSuggestFromChanges()
              }}
              disabled={
                generating ||
                committing ||
                success ||
                !isConnected ||
                writtenFiles.length === 0 ||
                !threadId.trim()
              }
              aria-label={t('commit.generateButton')}
              style={{
              width: '28px',
              height: '28px',
              borderRadius: '6px',
              border: '1px solid var(--border-default)',
              backgroundColor: 'transparent',
              color: 'var(--text-primary)',
              cursor:
                generating || committing || success || !isConnected || writtenFiles.length === 0 || !threadId.trim()
                  ? 'default'
                  : 'pointer',
              opacity:
                generating || committing || success || !isConnected || writtenFiles.length === 0 || !threadId.trim() ? 0.5 : 1,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              flexShrink: 0
            }}
          >
            {generating
              ? <Loader2 size={14} style={{ animation: 'spin 1s linear infinite' }} />
              : <Sparkles size={14} />}
            </button>
          </ActionTooltip>
        </div>

        <textarea
          ref={messageRef}
          value={message}
          onChange={(e) => setMessage(e.target.value)}
          onKeyDown={(e) => {
            if ((e.metaKey || e.ctrlKey) && e.key === 'Enter') {
              e.preventDefault()
              void handleSubmit()
            }
          }}
          disabled={committing || success || generating}
          rows={3}
          style={{
            width: '100%',
            boxSizing: 'border-box',
            padding: '8px 10px',
            fontSize: '13px',
            borderRadius: '6px',
            border: '1px solid var(--border-default)',
            background: 'var(--bg-primary)',
            color: 'var(--text-primary)',
            resize: 'vertical',
            outline: 'none',
            marginBottom: '8px',
            fontFamily: 'inherit',
            lineHeight: 1.5
          }}
          placeholder={t('commit.placeholderAuto')}
        />

        <style>
          {`@keyframes spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }`}
        </style>

        {error && (
          <div
            style={{
              padding: '8px 10px',
              borderRadius: '6px',
              background: 'var(--error-bg, rgba(255,80,80,0.1))',
              border: '1px solid var(--error)',
              color: 'var(--error)',
              fontSize: '12px',
              marginBottom: '12px',
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-word'
            }}
          >
            {error}
          </div>
        )}

        <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '8px' }}>
          <button
            onClick={() => { void handleSubmit() }}
            disabled={
              generating ||
              committing ||
              success ||
              writtenFiles.length === 0
            }
            style={{
              padding: '7px 16px',
              border: 'none',
              borderRadius: '6px',
              backgroundColor: 'var(--accent)',
              color: 'var(--on-accent)',
              fontSize: '13px',
              fontWeight: 500,
              display: 'flex',
              alignItems: 'center',
              gap: '6px',
              cursor:
                generating || committing || success || writtenFiles.length === 0 ? 'default' : 'pointer',
              opacity:
                generating || committing || success || writtenFiles.length === 0 ? 0.6 : 1
            }}
          >
            <GitCommit size={14} />
            {success
              ? t('commit.success')
              : committing
                ? t('commit.committing')
                : generating
                  ? t('commit.generating')
                  : t('commit.button')}
          </button>
        </div>
      </div>
    </div>
  )

  return createPortal(dialog, document.body) as JSX.Element
}

function toRelativePath(filePath: string, workspacePath: string): string {
  if (!workspacePath) return filePath
  const ws = workspacePath.replace(/\\/g, '/').replace(/\/$/, '')
  const fp = filePath.replace(/\\/g, '/')
  if (fp.startsWith(ws + '/')) return fp.slice(ws.length + 1)
  return filePath
}

function generateCommitMessage(turns: ReturnType<typeof useConversationStore.getState>['turns']): string {
  // Try to get the last agent message as a commit message suggestion
  for (let i = turns.length - 1; i >= 0; i--) {
    const turn = turns[i]
    for (let j = turn.items.length - 1; j >= 0; j--) {
      const item = turn.items[j]
      if (item.type === 'agentMessage' && item.text) {
        // Take first 72 chars of first non-empty line
        const firstLine = item.text.split('\n').find((l) => l.trim().length > 0) ?? ''
        const cleaned = firstLine.replace(/^[#*\->\s]+/, '').trim()
        if (cleaned) return cleaned.slice(0, 72)
      }
    }
  }
  return ''
}

