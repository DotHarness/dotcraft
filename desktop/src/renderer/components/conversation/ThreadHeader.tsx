import { useState, useRef, useEffect } from 'react'
import { Archive, FolderPlus, GitFork, MoreHorizontal, Pencil, Pin, PanelRightOpen } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { addToast, useToastStore } from '../../stores/toastStore'
import { CommitDialog, toRelativePath } from '../detail/CommitDialog'
import { CommitIcon } from '../ui/AppIcons'
import { OpenWorkspaceButton } from './OpenWorkspaceButton'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'
import { ThreadAppBindingsButton } from './ThreadAppBindingsButton'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { isSubAgentThread } from '../../utils/subAgentThreads'
import { canForkThread, canForkWorktree, runThreadFork } from '../../utils/threadFork'

interface ThreadHeaderProps {
  threadName: string
  threadId: string
  workspacePath: string
  remoteWorkspace?: boolean
}

/**
 * Fixed header bar at top of the conversation panel.
 * Shows thread name (double-click to rename inline), "Open" and "Commit" buttons.
 * Spec §10.2.
 */
export function ThreadHeader({
  threadName,
  threadId,
  workspacePath,
  remoteWorkspace = false
}: ThreadHeaderProps): JSX.Element {
  const t = useT()
  const [commitOpen, setCommitOpen] = useState(false)
  const [menuPosition, setMenuPosition] = useState<ContextMenuPosition | null>(null)
  const [renaming, setRenaming] = useState(false)
  const [renameValue, setRenameValue] = useState(threadName)
  const renameInputRef = useRef<HTMLInputElement>(null)
  const changedFiles = useConversationStore((s) => s.changedFiles)
  const detailPanelPreferredVisible = useUIStore((s) => s.detailPanelPreferredVisible)
  const toggleDetailPanel = useUIStore((s) => s.toggleDetailPanel)
  const activeThread = useThreadStore((s) => s.activeThread)
  const pinnedThreadIds = useThreadStore((s) => s.pinnedThreadIds)
  const togglePinnedThread = useThreadStore((s) => s.togglePinnedThread)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const setActiveThreadId = useThreadStore((s) => s.setActiveThreadId)
  const removeThreadTree = useThreadStore((s) => s.removeThreadTree)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const confirm = useConfirmDialog()

  const writtenFiles = Array.from(changedFiles.values()).filter((f) => f.status === 'written')
  const hasWrittenFiles = writtenFiles.length > 0
  const activeThreadIsSubAgent = activeThread ? isSubAgentThread(activeThread) : false
  const pinned = pinnedThreadIds.includes(threadId)
  const canFork = canForkThread(capabilities)
  const canForkIntoWorktree = canForkWorktree(capabilities)
  const worktreeBranch = activeThread?.worktree?.branchName?.trim()

  // Keep rename input value in sync when threadName changes externally
  useEffect(() => {
    if (!renaming) setRenameValue(threadName)
  }, [threadName, renaming])

  // Focus the input when entering rename mode
  useEffect(() => {
    if (renaming) {
      renameInputRef.current?.focus()
      renameInputRef.current?.select()
    }
  }, [renaming])

  function startRename(): void {
    setMenuPosition(null)
    setRenameValue(threadName)
    setRenaming(true)
  }

  async function commitRename(): Promise<void> {
    const newName = renameValue.trim()
    setRenaming(false)
    if (!newName || newName === threadName) return
    useThreadStore.getState().renameThread(threadId, newName)
    try {
      await window.api.appServer.sendRequest('thread/rename', {
        threadId,
        displayName: newName
      })
    } catch {
      // Roll back on failure
      useThreadStore.getState().renameThread(threadId, threadName)
    }
  }

  function cancelRename(): void {
    setRenaming(false)
    setRenameValue(threadName)
  }

  function handleRenameKeyDown(e: React.KeyboardEvent<HTMLInputElement>): void {
    if (e.key === 'Enter') { e.preventDefault(); void commitRename() }
    if (e.key === 'Escape') { e.preventDefault(); cancelRename() }
  }

  async function archiveThread(): Promise<void> {
    setMenuPosition(null)
    const ok = await confirm({
      title: t('threadEntry.archiveTitle'),
      message: t('threadEntry.archiveMessage'),
      confirmLabel: t('threadEntry.archiveConfirm')
    })
    if (!ok) return

    try {
      await window.api.appServer.sendRequest('thread/archive', { threadId })
    } catch {
      return
    }
    if (activeThreadId === threadId) setActiveThreadId(null)
    removeThreadTree(threadId)
  }

  function forkThread(mode: 'local' | 'worktree'): void {
    setMenuPosition(null)
    void runThreadFork({ threadId, mode, t })
  }

  /**
   * Runs the commit after the dialog has closed. All feedback flows through a
   * "Committing…" toast that is replaced by a success/error toast on
   * completion, so the user is never held on the dialog. Spec §16.5.
   */
  async function runCommit(message: string): Promise<void> {
    if (remoteWorkspace) {
      addToast(t('threadHeader.remoteLocalGitUnavailable'), 'warning')
      return
    }
    const files = Array.from(useConversationStore.getState().changedFiles.values()).filter(
      (f) => f.status === 'written'
    )
    if (files.length === 0) return

    // Persistent progress toast — cleared once the result lands.
    addToast(t('commit.committing'), 'info', 60_000)
    const committingId = useToastStore.getState().toasts.at(-1)?.id
    const clearCommitting = (): void => {
      if (committingId) useToastStore.getState().removeToast(committingId)
    }

    let finalMessage = message
    try {
      if (!finalMessage) {
        const isConnected = useConnectionStore.getState().status === 'connected'
        if (!isConnected) {
          clearCommitting()
          addToast(t('commit.generateTitle.disconnected'), 'error')
          return
        }
        const paths = files.map((f) => toRelativePath(f.filePath, workspacePath))
        const result = (await window.api.appServer.sendRequest(
          'workspace/commitMessage/suggest',
          { threadId, paths },
          120_000
        )) as { message?: string }
        if (!result?.message?.trim()) {
          clearCommitting()
          addToast(t('commit.error.emptyServer'), 'error')
          return
        }
        finalMessage = result.message.trim()
      }

      await window.api.git.commit(workspacePath, files.map((f) => f.filePath), finalMessage)
      clearCommitting()
      addToast(t('commit.toast.done', { line: finalMessage.split('\n')[0] }), 'success')
    } catch (err) {
      clearCommitting()
      addToast(t('commit.toast.failed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }
  }

  return (
    <>
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          padding: '10px 16px',
          flexShrink: 0,
          height: 'var(--chrome-header-height)',
          boxSizing: 'border-box'
        }}
      >
        {/* Thread name — double-click to rename */}
        {renaming ? (
          <input
            ref={renameInputRef}
            value={renameValue}
            onChange={(e) => setRenameValue(e.target.value)}
            onKeyDown={handleRenameKeyDown}
            onBlur={() => { void commitRename() }}
            aria-label={t('threadHeader.renameAria')}
            style={{
              flex: 1,
              fontSize: '14px',
              fontWeight: 600,
              color: 'var(--text-primary)',
              background: 'var(--bg-secondary)',
              border: '1px solid var(--border-active)',
              borderRadius: '4px',
              padding: '2px 6px',
              outline: 'none',
              fontFamily: 'inherit'
            }}
          />
        ) : (
          <ActionTooltip
            label={t('threadHeader.renameTitle')}
            placement="bottom"
            wrapperStyle={{ flex: 1, minWidth: 0 }}
          >
            <h1
              onDoubleClick={startRename}
              style={{
                margin: 0,
                fontSize: '14px',
                fontWeight: 600,
                color: 'var(--text-primary)',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
                cursor: 'default',
                userSelect: 'none'
              }}
            >
              <span
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '7px',
                  minWidth: 0
                }}
              >
                <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {threadName}
                </span>
                {worktreeBranch && (
                  <span
                    title={t('threadHeader.worktreeBadge', { branch: worktreeBranch })}
                    style={{
                      display: 'inline-flex',
                      alignItems: 'center',
                      maxWidth: '180px',
                      minWidth: 0,
                      height: '18px',
                      padding: '0 6px',
                      borderRadius: '999px',
                      border: '1px solid var(--border-default)',
                      color: 'var(--text-secondary)',
                      backgroundColor: 'var(--bg-secondary)',
                      fontSize: '11px',
                      fontWeight: 500,
                      lineHeight: 1,
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                      flexShrink: 1
                    }}
                  >
                    {worktreeBranch}
                  </span>
                )}
              </span>
            </h1>
          </ActionTooltip>
        )}

        <ActionTooltip label={t('threadHeader.moreActions')} placement="bottom">
          <button
            type="button"
            aria-label={t('threadHeader.moreActions')}
            onClick={(event) => {
              const rect = event.currentTarget.getBoundingClientRect()
              setMenuPosition({ x: rect.left, y: rect.bottom + 4 })
            }}
            style={iconButtonStyle}
            onMouseEnter={(e) => {
              ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-tertiary)'
              ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-primary)'
            }}
            onMouseLeave={(e) => {
              ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
              ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-secondary)'
            }}
          >
            <MoreHorizontal size={16} aria-hidden />
          </button>
        </ActionTooltip>

        {/* Open button */}
        {!remoteWorkspace && <OpenWorkspaceButton workspacePath={workspacePath} />}

        <ThreadAppBindingsButton threadId={threadId} />

        {/* Commit button */}
        <ActionTooltip
          label={t('threadHeader.commitTitle')}
          disabledReason={
            remoteWorkspace
              ? t('threadHeader.remoteLocalGitUnavailable')
              : !hasWrittenFiles
                ? t('threadHeader.noCommitTitle')
                : undefined
          }
          placement="bottom"
        >
          <button
            onClick={() => setCommitOpen(true)}
            disabled={remoteWorkspace || !hasWrittenFiles}
            style={{
              ...headerButtonStyle,
              opacity: !remoteWorkspace && hasWrittenFiles ? 1 : 0.4,
              cursor: !remoteWorkspace && hasWrittenFiles ? 'pointer' : 'default'
            }}
            aria-label={t('threadHeader.commitTitle')}
          >
            <CommitIcon size={13} />
            {t('threadHeader.commit')}
          </button>
        </ActionTooltip>

        {/* Panel toggle — only visible when panel is hidden (open-panel action).
            Closing is handled by the panel's own rightmost button. */}
        {!detailPanelPreferredVisible && (
          <ActionTooltip
            label={t('threadHeader.panelToggleShowLabel')}
            shortcut={ACTION_SHORTCUTS.toggleDetailPanel}
            placement="bottom"
          >
            <button
              onClick={toggleDetailPanel}
              aria-label={t('threadHeader.panelToggleShowLabel')}
              style={iconButtonStyle}
              onMouseEnter={(e) => {
                ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-tertiary)'
                ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-primary)'
              }}
              onMouseLeave={(e) => {
                ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
                ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-secondary)'
              }}
            >
              <PanelRightOpen size={16} aria-hidden />
            </button>
          </ActionTooltip>
        )}
      </div>

      {commitOpen && (
        <CommitDialog
          workspacePath={workspacePath}
          onCommit={(message) => {
            void runCommit(message)
          }}
          onClose={() => setCommitOpen(false)}
        />
      )}
      {menuPosition && (
        <ContextMenu
          position={menuPosition}
          onClose={() => setMenuPosition(null)}
          items={[
            ...(!activeThreadIsSubAgent
              ? [
                  {
                    label: pinned ? t('threadEntry.unpin') : t('threadEntry.pin'),
                    icon: <Pin size={14} aria-hidden />,
                    onClick: () => togglePinnedThread(threadId)
                  }
                ]
              : []),
            {
              label: t('threadEntry.rename'),
              icon: <Pencil size={14} aria-hidden />,
              onClick: startRename
            },
            ...(!activeThreadIsSubAgent
              ? [
                  {
                    label: t('threadEntry.archive'),
                    icon: <Archive size={14} aria-hidden />,
                    onClick: () => {
                      void archiveThread()
                    }
                  }
                ]
              : []),
            ...(!activeThreadIsSubAgent
              ? [
                  { type: 'separator' as const },
                  {
                    label: t('fork.menu'),
                    icon: <GitFork size={14} aria-hidden />,
                    disabled: !canFork,
                    title: canFork ? undefined : t('fork.unavailable'),
                    onClick: () => {},
                    submenu: [
                      {
                        label: t('fork.intoLocal'),
                        icon: <GitFork size={14} aria-hidden />,
                        onClick: () => forkThread('local')
                      },
                      ...(canForkIntoWorktree
                        ? [
                            {
                              label: t('fork.intoWorktree'),
                              icon: <FolderPlus size={14} aria-hidden />,
                              onClick: () => forkThread('worktree')
                            }
                          ]
                        : [])
                    ]
                  }
                ]
              : [])
          ]}
        />
      )}
    </>
  )
}

const headerButtonStyle: React.CSSProperties = {
  padding: '4px 10px',
  display: 'inline-flex',
  alignItems: 'center',
  gap: '6px',
  fontSize: '12px',
  fontWeight: 500,
  color: 'var(--text-secondary)',
  backgroundColor: 'transparent',
  border: '1px solid var(--border-default)',
  borderRadius: '6px',
  cursor: 'pointer',
  flexShrink: 0,
  transition: 'background-color 100ms ease, color 100ms ease'
}

// Shared ghost icon-button style used for the panel toggle on both sides
// (conversation header and detail panel tab bar): no border, transparent bg,
// hover-only highlight.
const iconButtonStyle: React.CSSProperties = {
  width: '28px',
  height: '28px',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 0,
  border: 'none',
  borderRadius: '6px',
  backgroundColor: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  flexShrink: 0,
  transition: 'background-color 100ms ease, color 100ms ease'
}
