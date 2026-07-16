import { useCallback, useEffect, useState, type CSSProperties, type JSX } from 'react'
import { Trash2 } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { useThreadStore } from '../../stores/threadStore'
import type { SessionIdentity, ThreadSummary } from '../../types/thread'
import { formatRelativeTime } from '../../utils/relativeTime'
import { isSubAgentThread } from '../../utils/subAgentThreads'
import { resolveDefaultCrossChannelOrigins } from '../../utils/visibleChannelsDefaults'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { ActionTooltip } from '../ui/ActionTooltip'
import { SettingsGroup, SettingsRow } from './SettingsGroup'
import { SettingsPanelShell } from './SettingsPanelShell'
import { Skeleton } from '../ui/Skeleton'

interface ArchivedThreadsSettingsViewProps {
  workspacePath?: string
  onThreadListRefreshRequested?: () => void
}

function actionButtonStyle(disabled = false): CSSProperties {
  return {
    height: '32px',
    padding: '0 12px',
    boxSizing: 'border-box',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: '8px',
    border: '1px solid var(--border-default)',
    background: 'transparent',
    color: 'var(--text-primary)',
    fontSize: '13px',
    fontWeight: 600,
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.7 : 1
  }
}

export function ArchivedThreadsSettingsView({
  workspacePath,
  onThreadListRefreshRequested
}: ArchivedThreadsSettingsViewProps): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const [threads, setThreads] = useState<ThreadSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [restoringIds, setRestoringIds] = useState<Set<string>>(new Set())
  const [deletingIds, setDeletingIds] = useState<Set<string>>(new Set())
  const [deletingAll, setDeletingAll] = useState(false)
  const [contextMenu, setContextMenu] = useState<{ threadId: string; position: ContextMenuPosition } | null>(null)
  const confirm = useConfirmDialog()

  const loadArchivedThreads = useCallback(async () => {
    if (!workspacePath) {
      setThreads([])
      setError(null)
      setLoading(false)
      return
    }

    setLoading(true)
    setError(null)
    try {
      const crossChannelOrigins = await resolveDefaultCrossChannelOrigins()
      const identity: SessionIdentity = {
        channelName: 'dotcraft-desktop',
        userId: 'local',
        channelContext: `workspace:${workspacePath}`,
        workspacePath
      }
      const result = await window.api.appServer.sendRequest('thread/list', {
        identity,
        includeArchived: true,
        crossChannelOrigins
      })
      const archivedThreads = ((result as { data?: ThreadSummary[] }).data ?? []).filter(
        (thread) => thread.status === 'archived' && !isSubAgentThread(thread)
      )
      setThreads(archivedThreads)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setThreads([])
    } finally {
      setLoading(false)
    }
  }, [workspacePath])

  useEffect(() => {
    void loadArchivedThreads()
  }, [loadArchivedThreads])

  async function handleRestore(threadId: string): Promise<void> {
    setRestoringIds((current) => {
      const next = new Set(current)
      next.add(threadId)
      return next
    })

    try {
      await window.api.appServer.sendRequest('thread/unarchive', { threadId })
      setThreads((current) => current.filter((thread) => thread.id !== threadId))
      useThreadStore.getState().updateThreadStatus(threadId, 'active')
      onThreadListRefreshRequested?.()
      addToast(t('archivedThreads.restoreSuccess'), 'success')
    } catch (err) {
      addToast(
        t('archivedThreads.restoreFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setRestoringIds((current) => {
        const next = new Set(current)
        next.delete(threadId)
        return next
      })
    }
  }

  async function confirmDeleteThread(threadId: string): Promise<void> {
    const ok = await confirm({
      title: t('archivedThreads.deleteConfirmTitle'),
      message: t('archivedThreads.deleteConfirmMessage'),
      confirmLabel: t('archivedThreads.delete'),
      danger: true
    })
    if (!ok) return
    setDeletingIds((current) => {
      const next = new Set(current)
      next.add(threadId)
      return next
    })
    try {
      await window.api.appServer.sendRequest('thread/delete', { threadId })
      setThreads((current) => current.filter((thread) => thread.id !== threadId))
      useThreadStore.getState().removeThreadTree(threadId)
      onThreadListRefreshRequested?.()
      addToast(t('archivedThreads.deleteSuccess'), 'success')
    } catch (err) {
      addToast(
        t('archivedThreads.deleteFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setDeletingIds((current) => {
        const next = new Set(current)
        next.delete(threadId)
        return next
      })
    }
  }

  async function confirmDeleteAll(): Promise<void> {
    if (threads.length === 0 || deletingAll) return
    const ok = await confirm({
      title: t('archivedThreads.deleteAllConfirmTitle'),
      message: t('archivedThreads.deleteAllConfirmMessage', { count: threads.length }),
      confirmLabel: t('archivedThreads.deleteAll'),
      danger: true
    })
    if (!ok) return
    setDeletingAll(true)
    const failures: string[] = []
    for (const thread of [...threads]) {
      try {
        await window.api.appServer.sendRequest('thread/delete', { threadId: thread.id })
        setThreads((current) => current.filter((item) => item.id !== thread.id))
        useThreadStore.getState().removeThreadTree(thread.id)
      } catch (err) {
        failures.push(thread.displayName?.trim() || thread.id)
        console.error('thread/delete failed:', err)
      }
    }
    onThreadListRefreshRequested?.()
    if (failures.length === 0) {
      addToast(t('archivedThreads.deleteAllSuccess'), 'success')
    } else {
      addToast(
        t('archivedThreads.bulkDeleteSummary', {
          success: Math.max(threads.length - failures.length, 0),
          failed: failures.length
        }),
        'warning'
      )
    }
    setDeletingAll(false)
  }

  return (
    <SettingsPanelShell
      title={t('archivedThreads.title')}
      description={t('archivedThreads.description')}
    >
      <SettingsGroup
        title={t('settings.group.conversations')}
        headerAction={
        !loading && !error ? (
          <button
            type="button"
            onClick={() => {
              void confirmDeleteAll()
            }}
            disabled={threads.length === 0 || deletingAll}
            style={{
              ...actionButtonStyle(threads.length === 0 || deletingAll),
              borderColor: 'color-mix(in srgb, var(--error) 46%, transparent)',
              color: 'var(--error)'
            }}
          >
            {deletingAll ? t('archivedThreads.deletingAll') : t('archivedThreads.deleteAll')}
          </button>
        ) : undefined
      }
      >
      {loading &&
        ['58%', '44%', '64%', '50%'].map((labelWidth, index) => (
          <SettingsRow
            key={`skeleton-${index}`}
            label={
              <span role={index === 0 ? 'status' : undefined} aria-label={index === 0 ? t('archivedThreads.loading') : undefined}>
                <Skeleton width={labelWidth} height={13} />
              </span>
            }
            description={<Skeleton width="34%" height={11} />}
            control={<Skeleton width={132} height={32} radius={8} />}
          />
        ))}

      {!loading && error && (
        <SettingsRow>
          <div style={{ fontSize: '13px', color: '#f85149' }}>
            {t('archivedThreads.loadFailed', { error })}
          </div>
        </SettingsRow>
      )}

      {!loading && !error && threads.length === 0 && (
        <SettingsRow>
          <div style={{ fontSize: '13px', color: 'var(--text-dimmed)' }}>{t('archivedThreads.empty')}</div>
        </SettingsRow>
      )}

      {!loading &&
        !error &&
        threads.length > 0 &&
        threads.map((thread) => {
          const displayName = thread.displayName?.trim() || t('sidebar.newConversation')
          const restoring = restoringIds.has(thread.id)
          const deleting = deletingIds.has(thread.id)
          return (
            <SettingsRow
              key={thread.id}
              onContextMenu={(e) => {
                e.preventDefault()
                setContextMenu({
                  threadId: thread.id,
                  position: { x: e.clientX, y: e.clientY }
                })
              }}
              label={
                <ActionTooltip
                  label={displayName}
                  wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}
                >
                  <span
                    style={{
                      display: 'block',
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap'
                    }}
                  >
                    {displayName}
                  </span>
                </ActionTooltip>
              }
              description={
                <>
                  <span>{formatRelativeTime(thread.lastActiveAt, new Date(), locale)}</span>
                  <span style={{ marginLeft: '10px' }}>
                    {t('archivedThreads.origin', { origin: thread.originChannel })}
                  </span>
                </>
              }
              control={
                <div style={{ display: 'inline-flex', alignItems: 'center', gap: '8px' }}>
                  <button
                    type="button"
                    onClick={() => {
                      void handleRestore(thread.id)
                    }}
                    disabled={restoring || deleting}
                    style={actionButtonStyle(restoring || deleting)}
                  >
                    {restoring ? t('archivedThreads.restoring') : t('archivedThreads.restore')}
                  </button>
                  <ActionTooltip
                    label={t('archivedThreads.delete')}
                    disabledReason={restoring || deleting ? t('archivedThreads.delete') : undefined}
                    placement="top"
                  >
                    <button
                      type="button"
                      onClick={() => {
                        void confirmDeleteThread(thread.id)
                      }}
                      disabled={restoring || deleting}
                      aria-label={t('archivedThreads.delete')}
                      style={{
                      width: '32px',
                      height: '32px',
                      borderRadius: '8px',
                      border: '1px solid color-mix(in srgb, var(--error) 46%, transparent)',
                      background: 'transparent',
                      color: 'var(--error)',
                      display: 'inline-flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      cursor: restoring || deleting ? 'default' : 'pointer',
                      opacity: restoring || deleting ? 0.7 : 1
                    }}
                  >
                    <Trash2 size={14} strokeWidth={2} aria-hidden />
                    </button>
                  </ActionTooltip>
                </div>
              }
            />
          )
        })}
      {contextMenu && (
        <ContextMenu
          position={contextMenu.position}
          onClose={() => setContextMenu(null)}
          items={[
            {
              label: t('archivedThreads.restore'),
              onClick: () => {
                void handleRestore(contextMenu.threadId)
              }
            },
            {
              label: t('archivedThreads.delete'),
              danger: true,
              onClick: () => {
                void confirmDeleteThread(contextMenu.threadId)
              }
            }
          ]}
        />
      )}
      </SettingsGroup>
    </SettingsPanelShell>
  )
}
