import { useDeferredValue, useMemo, useState, type JSX } from 'react'
import { Trash2 } from 'lucide-react'
import { useLocale, useT } from '../../../contexts/LocaleContext'
import { addToast } from '../../../stores/toastStore'
import { useThreadStore } from '../../../stores/threadStore'
import { useWorkspaceProjectsStore } from '../../../stores/workspaceProjectsStore'
import { formatRelativeTime } from '../../../utils/relativeTime'
import { CatalogFilterButton, CatalogSearchBox } from '../../catalog/CatalogSurface'
import { ContextMenu, type ContextMenuPosition } from '../../ui/ContextMenu'
import { useConfirmDialog } from '../../ui/ConfirmDialog'
import { ActionTooltip } from '../../ui/ActionTooltip'
import { Skeleton } from '../../ui/Skeleton'
import { Button } from '../../ui/Button'
import { IconButton } from '../../ui/IconButton'
import { MoreActionsButton } from '../../ui/MoreActionsButton'
import { SettingsGroup, SettingsRow } from '../SettingsGroup'
import { settingsPlaceholderStyle } from '../settingsTypography'
import { SettingsPanelShell } from '../SettingsPanelShell'
import {
  archivedThreadSources,
  groupArchivedThreads,
  isStartableSource,
  sourceFilter,
  type ArchivedProjectFilter,
  type ArchivedSort,
  type ArchivedThreadEntry,
  type ArchivedThreadGroup,
  type ArchivedThreadSource
} from './archivedThreadsModel'
import { useArchivedThreads } from './useArchivedThreads'
import css from './ArchivedThreadsSettingsView.module.css'

interface ArchivedThreadsSettingsViewProps {
  onThreadListRefreshRequested?: () => void
}

const SORTS: ArchivedSort[] = ['updated', 'created', 'title']

export function ArchivedThreadsSettingsView({
  onThreadListRefreshRequested
}: ArchivedThreadsSettingsViewProps): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const projects = useWorkspaceProjectsStore((state) => state.projects)
  const chat = useWorkspaceProjectsStore((state) => state.chat)
  const sources = useMemo(() => archivedThreadSources({ projects, chat }), [projects, chat])
  const { states, reload, removeThread } = useArchivedThreads(sources)
  const [query, setQuery] = useState('')
  const deferredQuery = useDeferredValue(query)
  const [projectFilter, setProjectFilter] = useState<ArchivedProjectFilter>('all')
  const [sort, setSort] = useState<ArchivedSort>('updated')
  const [pendingIds, setPendingIds] = useState<Set<string>>(new Set())
  const [deletingAll, setDeletingAll] = useState(false)
  const [startingKey, setStartingKey] = useState<string | null>(null)
  const [contextMenu, setContextMenu] = useState<{ entry: ArchivedThreadEntry; position: ContextMenuPosition } | null>(null)
  const [groupMenu, setGroupMenu] = useState<{ group: ArchivedThreadGroup; position: ContextMenuPosition } | null>(null)
  const confirm = useConfirmDialog()

  const selectedSource = sources.find((source) => sourceFilter(source) === projectFilter)
  const effectiveFilter: ArchivedProjectFilter = projectFilter.startsWith('source:') && !selectedSource ? 'all' : projectFilter
  const threadsBySource = useMemo(
    () => new Map([...states].map(([key, state]) => [key, state.threads])),
    [states]
  )
  const groups = useMemo(
    () => groupArchivedThreads(sources, threadsBySource, { query: deferredQuery, project: effectiveFilter, sort }),
    [sources, threadsBySource, deferredQuery, effectiveFilter, sort]
  )
  const visibleEntries = groups.flatMap((group) => group.entries)
  const scopedSources = selectedSource ? [selectedSource] : sources
  const loading = visibleEntries.length === 0 && scopedSources.some((source) =>
    source.live && states.get(source.key)?.status !== 'ready' && states.get(source.key)?.status !== 'error')
  const failedSources = scopedSources.filter((source) => states.get(source.key)?.status === 'error')
  const coldSources = sources.filter(isStartableSource)
  const sourceLabel = (source: ArchivedThreadSource): string =>
    source.kind === 'chat' ? t('archivedThreads.project.chats') : source.name

  function setPending(threadId: string, pending: boolean): void {
    setPendingIds((current) => {
      const next = new Set(current)
      if (pending) next.add(threadId)
      else next.delete(threadId)
      return next
    })
  }

  async function deleteEntry(entry: ArchivedThreadEntry): Promise<void> {
    await window.api.appServer.sendConnectionRequest(entry.source.key, 'thread/delete', { threadId: entry.thread.id })
    removeThread(entry.source.key, entry.thread.id)
    if (entry.source.foreground) useThreadStore.getState().removeThreadTree(entry.thread.id)
  }

  async function handleRestore(entry: ArchivedThreadEntry): Promise<void> {
    setPending(entry.thread.id, true)
    try {
      await window.api.appServer.sendConnectionRequest(entry.source.key, 'thread/unarchive', { threadId: entry.thread.id })
      removeThread(entry.source.key, entry.thread.id)
      if (entry.source.foreground) {
        useThreadStore.getState().updateThreadStatus(entry.thread.id, 'active')
        onThreadListRefreshRequested?.()
      }
      addToast(t('archivedThreads.restoreSuccess'), 'success')
    } catch (err) {
      addToast(t('archivedThreads.restoreFailed', { error: errorText(err) }), 'error')
    } finally {
      setPending(entry.thread.id, false)
    }
  }

  async function confirmDeleteThread(entry: ArchivedThreadEntry): Promise<void> {
    const ok = await confirm({
      title: t('archivedThreads.deleteConfirmTitle'),
      message: t('archivedThreads.deleteConfirmMessage'),
      confirmLabel: t('archivedThreads.delete'),
      danger: true
    })
    if (!ok) return
    setPending(entry.thread.id, true)
    try {
      await deleteEntry(entry)
      if (entry.source.foreground) onThreadListRefreshRequested?.()
      addToast(t('archivedThreads.deleteSuccess'), 'success')
    } catch (err) {
      addToast(t('archivedThreads.deleteFailed', { error: errorText(err) }), 'error')
    } finally {
      setPending(entry.thread.id, false)
    }
  }

  async function confirmDeleteEntries(entries: ArchivedThreadEntry[], title: string): Promise<void> {
    if (entries.length === 0 || deletingAll) return
    const ok = await confirm({
      title,
      message: t('archivedThreads.deleteAllConfirmMessage', { count: entries.length }),
      confirmLabel: t('archivedThreads.deleteAll'),
      danger: true
    })
    if (!ok) return
    setDeletingAll(true)
    let failed = 0
    for (const entry of entries) {
      try {
        await deleteEntry(entry)
      } catch (err) {
        failed += 1
        console.error('thread/delete failed:', err)
      }
    }
    if (entries.some((entry) => entry.source.foreground)) onThreadListRefreshRequested?.()
    if (failed === 0) {
      addToast(t('archivedThreads.deleteManySuccess', { count: entries.length }), 'success')
    } else {
      addToast(t('archivedThreads.bulkDeleteSummary', { success: entries.length - failed, failed }), 'warning')
    }
    setDeletingAll(false)
  }

  async function startSource(source: ArchivedThreadSource): Promise<void> {
    setStartingKey(source.key)
    try {
      await window.api.workspace.startProject(source.path)
    } catch (err) {
      addToast(t('archivedThreads.startFailed', { error: errorText(err) }), 'error')
    } finally {
      setStartingKey(null)
    }
  }

  function groupLabel(group: ArchivedThreadGroup): string {
    return group.source ? sourceLabel(group.source) : t('archivedThreads.project.automations')
  }

  function renderGroupActions(group: ArchivedThreadGroup): JSX.Element {
    const name = groupLabel(group)
    const count = group.entries.length
    return (
      <div className={css.groupActions}>
        <span className={css.groupCount}>
          {t(count === 1 ? 'archivedThreads.groupCount.one' : 'archivedThreads.groupCount.other', { count })}
        </span>
        <MoreActionsButton
          size={28}
          label={t('archivedThreads.groupActions', { name })}
          disabled={deletingAll}
          open={groupMenu?.group.id === group.id}
          onClick={(event) => {
            const rect = event.currentTarget.getBoundingClientRect()
            setGroupMenu({ group, position: { x: rect.right - 200, y: rect.bottom + 4 } })
          }}
        />
      </div>
    )
  }

  function renderRow(entry: ArchivedThreadEntry, group: ArchivedThreadGroup): JSX.Element {
    const { thread } = entry
    const displayName = thread.displayName?.trim() || t('sidebar.newThreadLabel')
    const pending = pendingIds.has(thread.id) || deletingAll
    const time = sort === 'created' ? thread.createdAt : thread.lastActiveAt
    return (
      <SettingsRow
        key={`${entry.source.key}:${thread.id}`}
        onContextMenu={(e) => {
          e.preventDefault()
          setContextMenu({ entry, position: { x: e.clientX, y: e.clientY } })
        }}
        label={
          <ActionTooltip label={displayName} wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}>
            <span className={css.title}>{displayName}</span>
          </ActionTooltip>
        }
        description={
          <span className={css.meta}>
            <span>{formatRelativeTime(time, new Date(), locale)}</span>
            {group.kind === 'automations'
              ? <span>{sourceLabel(entry.source)}</span>
              : <span>{t('archivedThreads.origin', { origin: thread.originChannel })}</span>}
          </span>
        }
        control={
          <div className={css.actions}>
            <IconButton
              tone="danger"
              icon={<Trash2 size={14} aria-hidden />}
              label={t('archivedThreads.delete')}
              tooltipLabel={t('archivedThreads.delete')}
              tooltipPlacement="top"
              disabled={pending}
              disabledReason={t('archivedThreads.delete')}
              onClick={() => { void confirmDeleteThread(entry) }}
            />
            <Button onClick={() => { void handleRestore(entry) }} disabled={pending}>
              {pendingIds.has(thread.id) ? t('archivedThreads.restoring') : t('archivedThreads.restore')}
            </Button>
          </div>
        }
      />
    )
  }

  const projectOptions = [
    { value: 'all', label: t('archivedThreads.project.all') },
    ...sources.map((source) => ({
      value: sourceFilter(source),
      label: isStartableSource(source)
        ? t('archivedThreads.project.notRunning', { name: sourceLabel(source) })
        : sourceLabel(source)
    })),
    { value: 'automations', label: t('archivedThreads.project.automations') }
  ]

  return (
    <SettingsPanelShell
      title={t('archivedThreads.title')}
      description={t('archivedThreads.description')}
      action={
        <Button
          variant="danger"
          iconLeft={<Trash2 size={15} />}
          onClick={() => { void confirmDeleteEntries(visibleEntries, t('archivedThreads.deleteAllConfirmTitle')) }}
          disabled={visibleEntries.length === 0 || deletingAll}
        >
          {deletingAll ? t('archivedThreads.deletingAll') : t('archivedThreads.deleteAll')}
        </Button>
      }
    >
      <div className={css.toolbar}>
        <CatalogSearchBox value={query} placeholder={t('archivedThreads.search')} onChange={setQuery} />
        <CatalogFilterButton
          ariaLabel={t('archivedThreads.filter')}
          groups={[
            {
              label: t('archivedThreads.filter.project'),
              value: effectiveFilter,
              options: projectOptions,
              onChange: (value) => setProjectFilter(value as ArchivedProjectFilter),
              isActive: effectiveFilter !== 'all'
            },
            {
              label: t('archivedThreads.filter.sort'),
              value: sort,
              options: SORTS.map((value) => ({ value, label: t(`archivedThreads.sort.${value}`) })),
              onChange: (value) => setSort(value as ArchivedSort),
              isActive: sort !== 'updated'
            }
          ]}
        />
      </div>

      {failedSources.length > 0 && (
        <SettingsGroup>
          {failedSources.map((source) => (
            <SettingsRow
              key={source.key}
              label={<span className={css.error}>{t('archivedThreads.sourceFailed', { name: sourceLabel(source) })}</span>}
              description={states.get(source.key)?.error}
              control={<Button onClick={() => reload(source.key)}>{t('common.retry')}</Button>}
            />
          ))}
        </SettingsGroup>
      )}

      {selectedSource && !selectedSource.live && selectedSource.kind === 'local' && (
        <SettingsGroup>
          <SettingsRow
            label={sourceLabel(selectedSource)}
            description={t('archivedThreads.notRunning')}
            control={
              <Button
                onClick={() => { void startSource(selectedSource) }}
                disabled={!isStartableSource(selectedSource) || startingKey === selectedSource.key}
              >
                {isStartableSource(selectedSource) && startingKey !== selectedSource.key
                  ? t('archivedThreads.startProject')
                  : t('archivedThreads.starting')}
              </Button>
            }
          />
        </SettingsGroup>
      )}

      {loading && (
        <SettingsGroup>
          {['58%', '44%', '64%', '50%'].map((labelWidth, index) => (
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
        </SettingsGroup>
      )}

      {!loading && groups.length === 0 && failedSources.length === 0 && (selectedSource?.live ?? true) && (
        <SettingsGroup>
          <SettingsRow>
            <div style={settingsPlaceholderStyle()}>
              {deferredQuery.trim() ? t('archivedThreads.noMatches') : t('archivedThreads.empty')}
            </div>
          </SettingsRow>
        </SettingsGroup>
      )}

      {groups.map((group) => (
        <SettingsGroup
          key={group.id}
          title={effectiveFilter === 'all' ? groupLabel(group) : undefined}
          headerAction={effectiveFilter === 'all' ? renderGroupActions(group) : undefined}
        >
          {group.entries.map((entry) => renderRow(entry, group))}
        </SettingsGroup>
      ))}

      {effectiveFilter === 'all' && coldSources.length > 0 && (
        <p className={css.hint}>{t('archivedThreads.coldHint', { count: coldSources.length })}</p>
      )}

      {groupMenu && (
        <ContextMenu
          position={groupMenu.position}
          onClose={() => setGroupMenu(null)}
          items={[{
            label: t('archivedThreads.deleteGroup'),
            icon: <Trash2 size={14} aria-hidden />,
            danger: true,
            onClick: () => {
              void confirmDeleteEntries(
                groupMenu.group.entries,
                t('archivedThreads.deleteGroupConfirmTitle', { name: groupLabel(groupMenu.group) })
              )
            }
          }]}
        />
      )}

      {contextMenu && (
        <ContextMenu
          position={contextMenu.position}
          onClose={() => setContextMenu(null)}
          items={[
            { label: t('archivedThreads.restore'), onClick: () => { void handleRestore(contextMenu.entry) } },
            { label: t('archivedThreads.delete'), danger: true, onClick: () => { void confirmDeleteThread(contextMenu.entry) } }
          ]}
        />
      )}
    </SettingsPanelShell>
  )
}

function errorText(err: unknown): string {
  return err instanceof Error ? err.message : String(err)
}