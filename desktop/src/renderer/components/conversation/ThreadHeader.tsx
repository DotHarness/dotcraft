import { useState, useRef, useEffect } from 'react'
import { Archive, ArrowRightLeft, GitFork, Laptop, MoreHorizontal, Pencil, Pin, PanelRightOpen } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useSourceControlStore } from '../../stores/sourceControlStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { addToast, useToastStore } from '../../stores/toastStore'
import { CommitDialog, toRelativePath } from '../detail/CommitDialog'
import { PerforcePrepareDialog } from '../detail/PerforcePrepareDialog'
import { CommitIcon } from '../ui/AppIcons'
import { usePerforceChangelistStore, type PerforceChangelistEntry } from '../../stores/perforceChangelistStore'
import { OpenWorkspaceButton } from './OpenWorkspaceButton'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'
import { ThreadAppBindingsButton } from './ThreadAppBindingsButton'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { IconButton } from '../ui/IconButton'
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
  const [prepareOpen, setPrepareOpen] = useState(false)
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
  const sourceControlEnabled = capabilities?.sourceControlManagement === true
  const ensureSourceControl = useSourceControlStore((s) => s.ensure)
  const sourceControlProvider = useSourceControlStore((s) =>
    s.workspacePath === workspacePath ? s.effectiveProvider : null
  )
  const perforceChangelistAvailable = useSourceControlStore((s) =>
    s.workspacePath === workspacePath ? s.perforceChangelist === true : false
  )
  const threadSourceControlProvider = typeof activeThread?.metadata?.['sourceControl.provider'] === 'string'
    ? activeThread.metadata['sourceControl.provider']
    : null
  const isPerforceWorkspace = sourceControlProvider === 'perforce'
    || (sourceControlProvider == null && threadSourceControlProvider === 'perforce')
  const canPreparePerforce = sourceControlProvider === 'perforce' && perforceChangelistAvailable
  const metadataChangelist = typeof activeThread?.metadata?.['sourceControl.perforce.changelist'] === 'string'
    ? activeThread.metadata['sourceControl.perforce.changelist']
    : 'default'
  const changelistState = usePerforceChangelistStore((s) => s.byThreadId[threadId])
  const changelistSnapshot = changelistState?.snapshot ?? null
  const selectedChangelist = changelistSnapshot?.target?.changelist ?? metadataChangelist
  const prepareChangelists: PerforceChangelistEntry[] = changelistSnapshot?.changelists.length
    ? changelistSnapshot.changelists
    : [
        { id: 'default', isDefault: true, description: '', user: '', client: '', status: 'pending' },
        ...(selectedChangelist !== 'default'
          ? [{ id: selectedChangelist, isDefault: false, description: '', user: '', client: '', status: 'pending' }]
          : [])
      ]

  useEffect(() => {
    ensureSourceControl(workspacePath, sourceControlEnabled)
  }, [ensureSourceControl, workspacePath, sourceControlEnabled])

  useEffect(() => {
    if (!canPreparePerforce) return
    void usePerforceChangelistStore.getState().ensure(threadId)
  }, [canPreparePerforce, threadId])

  const writtenFiles = Array.from(changedFiles.values()).filter((f) => f.status === 'written')
  const hasWrittenFiles = writtenFiles.length > 0
  const activeThreadIsSubAgent = activeThread ? isSubAgentThread(activeThread) : false
  const pinned = pinnedThreadIds.includes(threadId)
  const canFork = canForkThread(capabilities)
  const canForkIntoWorktree = canForkWorktree(capabilities) && !remoteWorkspace
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
    // One-click archive: archived threads are restorable anytime from
    // Settings → Archived Threads, so no extra confirmation is needed.
    setMenuPosition(null)
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
    if (isPerforceWorkspace) {
      return
    }
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

  async function runPrepareChangelist(description: string, target: string): Promise<void> {
    if (!canPreparePerforce) {
      addToast(t('perforcePrepare.toast.offline'), 'warning')
      return
    }
    const files = Array.from(useConversationStore.getState().changedFiles.values()).filter(
      (f) => f.status === 'written'
    )
    if (files.length === 0) return

    addToast(t('perforcePrepare.preparing'), 'info', 60_000)
    const preparingId = useToastStore.getState().toasts.at(-1)?.id
    const clearPreparing = (): void => {
      if (preparingId) useToastStore.getState().removeToast(preparingId)
    }

    try {
      const paths = files.map((f) => toRelativePath(f.filePath, workspacePath))
      let finalDescription = description
      const prepareTarget = target.trim() || selectedChangelist
      if (!finalDescription) {
        const isConnected = useConnectionStore.getState().status === 'connected'
        if (!isConnected) {
          clearPreparing()
          addToast(t('commit.generateTitle.disconnected'), 'error')
          return
        }
        const suggest = await window.api.appServer.sendRequest(
          'workspace/commitMessage/suggest',
          { threadId, paths, provider: 'perforce' },
          120_000
        ) as { message?: string }
        if (!suggest?.message?.trim()) {
          clearPreparing()
          addToast(t('commit.error.emptyServer'), 'error')
          return
        }
        finalDescription = suggest.message.trim()
      }

      const result = await window.api.appServer.sendRequest(
        'sourceControl/changelist/prepare',
        {
          threadId,
          description: finalDescription,
          paths,
          target: prepareTarget
        },
        60_000
      ) as {
        status?: string
        changelist?: string
        movedPaths?: string[]
        skippedPaths?: string[]
        warnings?: Array<{ code?: string, fallbackText?: string }>
        errors?: Array<{ code?: string, fallbackText?: string }>
      }
      clearPreparing()
      if (result.status === 'error') {
        const message = result.errors?.[0]?.fallbackText || t('perforcePrepare.toast.failedUnknown')
        addToast(t('perforcePrepare.toast.failed', { error: message }), 'error')
        return
      }
      await usePerforceChangelistStore.getState().ensure(threadId, { force: true })
      const warning = result.warnings?.[0]?.fallbackText
      addToast(
        warning
          ? t('perforcePrepare.toast.doneWithWarning', { changelist: result.changelist ?? prepareTarget, warning })
          : t('perforcePrepare.toast.done', { changelist: result.changelist ?? prepareTarget }),
        warning ? 'warning' : 'success'
      )
    } catch (err) {
      clearPreparing()
      addToast(t('perforcePrepare.toast.failed', { error: err instanceof Error ? err.message : String(err) }), 'error')
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
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '4px',
              flex: 1,
              minWidth: 0
            }}
          >
            <ActionTooltip
              label={t('threadHeader.renameTitle')}
              placement="bottom"
              wrapperStyle={{ flex: '0 1 auto', minWidth: 0, maxWidth: '100%' }}
            >
              <h1
                onDoubleClick={startRename}
                style={{
                  margin: 0,
                  minWidth: 0,
                  maxWidth: '100%',
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
                    minWidth: 0,
                    maxWidth: '100%',
                    overflow: 'hidden'
                  }}
                >
                  <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {threadName}
                  </span>
                  {worktreeBranch && (
                    <ActionTooltip
                      label={t('threadHeader.worktreeBadge', { branch: worktreeBranch })}
                      wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}
                    >
                      <span
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
                    </ActionTooltip>
                  )}
                </span>
              </h1>
            </ActionTooltip>

            <IconButton
                size={28}
                label={t('threadHeader.moreActions')}
                tooltipPlacement="bottom"
                tooltipLabel={t('threadHeader.moreActions')}
                icon={<MoreHorizontal size={16} aria-hidden />}
                aria-haspopup="menu"
                aria-expanded={menuPosition != null}
                onClick={(event) => {
                  const rect = event.currentTarget.getBoundingClientRect()
                  setMenuPosition({ x: rect.left, y: rect.bottom + 4 })
                }}
              />
          </div>
        )}

        {/* Open button */}
        {!remoteWorkspace && <OpenWorkspaceButton workspacePath={workspacePath} />}

        <ThreadAppBindingsButton threadId={threadId} />

        {/* Commit / Perforce prepare action */}
        <IconButton
          size={28}
          label={isPerforceWorkspace ? t('threadHeader.prepareChangelistTitle') : t('threadHeader.commitTitle')}
          tooltipLabel={isPerforceWorkspace ? t('threadHeader.prepareChangelistTitle') : t('threadHeader.commitTitle')}
          disabledReason={
            isPerforceWorkspace
              ? (!canPreparePerforce
                  ? t('threadHeader.prepareChangelistUnavailableTitle')
                  : !hasWrittenFiles
                    ? t('threadHeader.noPrepareChangelistTitle')
                    : undefined)
              : remoteWorkspace
                ? t('threadHeader.remoteLocalGitUnavailable')
                : !hasWrittenFiles
                  ? t('threadHeader.noCommitTitle')
                  : undefined
          }
          tooltipPlacement="bottom"
          onClick={() => {
            if (isPerforceWorkspace) {
              if (canPreparePerforce) setPrepareOpen(true)
            }
            else setCommitOpen(true)
          }}
          disabled={isPerforceWorkspace ? !canPreparePerforce || !hasWrittenFiles : remoteWorkspace || !hasWrittenFiles}
          icon={<CommitIcon size={14} />}
        />

        {/* Panel toggle — only visible when panel is hidden (open-panel action).
            Closing is handled by the panel's own rightmost button. */}
        {!detailPanelPreferredVisible && (
          <IconButton
            size={28}
            label={t('threadHeader.panelToggleShowLabel')}
            tooltipLabel={t('threadHeader.panelToggleShowLabel')}
            shortcut={ACTION_SHORTCUTS.toggleDetailPanel}
            tooltipPlacement="bottom"
            onClick={toggleDetailPanel}
            icon={<PanelRightOpen size={16} aria-hidden />}
          />
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
      {prepareOpen && (
        <PerforcePrepareDialog
          workspacePath={workspacePath}
          changelist={selectedChangelist}
          changelists={prepareChangelists}
          onPrepare={(description, target) => {
            void runPrepareChangelist(description, target)
          }}
          onClose={() => setPrepareOpen(false)}
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
            ...(!activeThreadIsSubAgent && canFork
              ? [
                  { type: 'separator' as const },
                  {
                    label: t('fork.menu'),
                    icon: <GitFork size={14} aria-hidden />,
                    onClick: () => {},
                    submenu: [
                      {
                        label: t('fork.intoLocal'),
                        icon: <Laptop size={14} aria-hidden />,
                        onClick: () => forkThread('local')
                      },
                      ...(canForkIntoWorktree
                        ? [
                            {
                              label: t('fork.intoWorktree'),
                              icon: <ArrowRightLeft size={14} aria-hidden />,
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
