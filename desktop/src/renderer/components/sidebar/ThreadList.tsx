import { useShallow } from 'zustand/react/shallow'
import { useT } from '../../contexts/LocaleContext'
import { useDragDropStore } from '../../stores/dragDropStore'
import { useThreadStore, selectFilteredThreads } from '../../stores/threadStore'
import { groupThreads } from '../../utils/threadGrouping'
import { ThreadGroup } from './ThreadGroup'
import type { ThreadGroup as ThreadGroupType } from '../../types/thread'
import type { ThreadSummary } from '../../types/thread'
import { THREAD_GROUP_ORDER } from '../../types/thread'
import { getSubAgentParentThreadId, isSubAgentThread } from '../../utils/subAgentThreads'

/**
 * Scrollable container for the grouped thread list.
 * Handles empty states for "no threads" and "no search results".
 * Spec §9.5
 */
export function ThreadList(): JSX.Element {
  const t = useT()
  const { threadList, searchQuery, loading, pinnedThreadIds } = useThreadStore()
  // useShallow prevents infinite re-renders: selectFilteredThreads returns a new
  // array on every call (via .filter), so without shallow equality Zustand's
  // useSyncExternalStore sees a changed snapshot every render and loops.
  const filteredThreads = useThreadStore(useShallow(selectFilteredThreads))
  const dragActive = useDragDropStore((s) => s.active)
  const dragHintTitle =
    dragActive?.kind === 'automation-task' ? dragActive.title : null

  if (loading) {
    return (
      <div style={emptyStyle}>
        <span style={{
          color: 'var(--text-dimmed)',
          fontSize: 'var(--type-ui-size)',
          lineHeight: 'var(--type-ui-line-height)'
        }}>{t('threadList.loading')}</span>
      </div>
    )
  }

  if (threadList.length === 0) {
    return (
      <div style={emptyStyle}>
        <span style={{
          color: 'var(--text-dimmed)',
          fontSize: 'var(--type-ui-size)',
          lineHeight: 'var(--type-ui-line-height)',
          textAlign: 'center'
        }}>
          {t('threadList.empty')}
          <br />
          {t('threadList.emptyHint', { label: t('sidebar.newThreadLabel') })}
        </span>
      </div>
    )
  }

  if (filteredThreads.length === 0 && searchQuery) {
    return (
      <div style={emptyStyle}>
        <span style={{
          color: 'var(--text-dimmed)',
          fontSize: 'var(--type-ui-size)',
          lineHeight: 'var(--type-ui-line-height)'
        }}>
          {t('threadList.noSearchResults')}
        </span>
      </div>
    )
  }

  const orderedThreads = orderSubAgentsAfterParents(filteredThreads)
  const { pinnedThreads, unpinnedThreads } = partitionPinnedThreads(
    orderedThreads,
    pinnedThreadIds
  )
  const groups = groupThreads(unpinnedThreads)

  return (
    <div
      style={{
        flex: 1,
        overflowY: 'auto',
        overflowX: 'hidden',
        paddingBottom: '8px',
        scrollbarWidth: 'thin',
        scrollbarColor: 'var(--border-default) transparent',
        position: 'relative'
      }}
    >
      {dragHintTitle !== null && (
        <div
          aria-hidden="true"
          style={{
            position: 'sticky',
            top: 0,
            zIndex: 2,
            margin: '4px 10px 6px',
            padding: '6px 10px',
            borderRadius: '999px',
            fontSize: 'var(--type-secondary-size)',
            lineHeight: 'var(--type-secondary-line-height)',
            fontWeight: 'var(--type-ui-emphasis-weight)',
            color: 'var(--accent)',
            backgroundColor: 'color-mix(in srgb, var(--accent) 12%, var(--bg-secondary))',
            border: '1px solid color-mix(in srgb, var(--accent) 30%, transparent)',
            boxShadow: '0 2px 8px color-mix(in srgb, var(--accent) 12%, transparent)',
            whiteSpace: 'nowrap',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            pointerEvents: 'none',
            animation: 'fadeSlideDown 160ms ease'
          }}
        >
          {t('auto.dnd.hintBar', { title: dragHintTitle })}
        </div>
      )}
      {pinnedThreads.length > 0 && (
        <ThreadGroup label="Pinned" threads={pinnedThreads} />
      )}
      {(THREAD_GROUP_ORDER as ThreadGroupType[])
        .filter((g) => groups.has(g))
        .map((group) => (
          <ThreadGroup key={group} label={group} threads={groups.get(group)!} />
        ))}
    </div>
  )
}

function orderSubAgentsAfterParents(threads: ThreadSummary[]): ThreadSummary[] {
  const childrenByParent = new Map<string, ThreadSummary[]>()
  const topLevel: ThreadSummary[] = []
  const emitted = new Set<string>()

  for (const thread of threads) {
    const parentId = isSubAgentThread(thread) ? getSubAgentParentThreadId(thread) : null
    if (parentId) {
      const children = childrenByParent.get(parentId) ?? []
      children.push(thread)
      childrenByParent.set(parentId, children)
    } else {
      topLevel.push(thread)
    }
  }

  const result: ThreadSummary[] = []
  for (const thread of topLevel) {
    result.push(thread)
    emitted.add(thread.id)
    const children = childrenByParent.get(thread.id) ?? []
    for (const child of children) {
      result.push(child)
      emitted.add(child.id)
    }
  }

  for (const thread of threads) {
    if (!emitted.has(thread.id)) {
      result.push(thread)
      emitted.add(thread.id)
    }
  }

  return result
}

function partitionPinnedThreads(
  threads: ThreadSummary[],
  pinnedThreadIds: string[]
): { pinnedThreads: ThreadSummary[]; unpinnedThreads: ThreadSummary[] } {
  if (pinnedThreadIds.length === 0 || threads.length === 0) {
    return { pinnedThreads: [], unpinnedThreads: threads }
  }

  const byId = new Map(threads.map((thread) => [thread.id, thread]))
  const childrenByParent = new Map<string, ThreadSummary[]>()
  for (const thread of threads) {
    const parentId = isSubAgentThread(thread) ? getSubAgentParentThreadId(thread) : null
    if (!parentId) continue
    const children = childrenByParent.get(parentId) ?? []
    children.push(thread)
    childrenByParent.set(parentId, children)
  }

  const included = new Set<string>()
  const pinnedThreads: ThreadSummary[] = []

  function appendThreadTree(threadId: string): void {
    if (included.has(threadId)) return
    const thread = byId.get(threadId)
    if (!thread) return
    included.add(threadId)
    pinnedThreads.push(thread)
    for (const child of childrenByParent.get(threadId) ?? []) {
      appendThreadTree(child.id)
    }
  }

  for (const threadId of pinnedThreadIds) {
    const thread = byId.get(threadId)
    if (!thread || isSubAgentThread(thread)) continue
    appendThreadTree(threadId)
  }

  return {
    pinnedThreads,
    unpinnedThreads: threads.filter((thread) => !included.has(thread.id))
  }
}

const emptyStyle: React.CSSProperties = {
  flex: 1,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: '24px 16px'
}
