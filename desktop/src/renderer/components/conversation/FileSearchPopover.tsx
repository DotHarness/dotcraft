import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { MessagesSquare } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useThreadStore } from '../../stores/threadStore'
import type { ThreadSummary } from '../../types/thread'
import { fuzzyRank } from '../../utils/fuzzyMatch'
import { searchThreadMentions, threadMentionTitle } from '../../utils/threadReferences'
import { ActionTooltip } from '../ui/ActionTooltip'
import { FileTypeIcon } from '../ui/FileTypeIcon'
import type { ThreadMentionSource } from './useComposerThreadReferences'
import { useThreadContentSearch } from './useThreadContentSearch'
import {
  MentionRowIcon,
  mentionEmptyStyle,
  MentionPopoverSurface,
  mentionRowDescStyle,
  mentionRowNameStyle,
  mentionRowStyle
} from './mentionPopoverUi'

const DEBOUNCE_MS = 80
const INDEX_POLL_MS = 1500
const MAX_RESULTS = 8

export interface FileMatch {
  name: string
  relativePath: string
  dir: string
}

type IndexStatus = 'empty' | 'building' | 'ready'

type MentionEntry = { kind: 'file'; file: FileMatch } | { kind: 'thread'; thread: ThreadSummary }

interface FileSearchPopoverProps {
  query: string
  visible: boolean
  workspacePath: string
  onSelect: (relativePath: string) => void
  onDismiss: () => void
  threadMentions?: ThreadMentionSource
}

export function FileSearchPopover({
  query,
  visible,
  workspacePath,
  onSelect,
  onDismiss,
  threadMentions
}: FileSearchPopoverProps): JSX.Element | null {
  const t = useT()
  const threadList = useThreadStore((s) => s.threadList)
  const excludedKey = visible ? threadMentions?.excludedThreadIds().join('\0') : undefined
  const [loading, setLoading] = useState(false)
  const [files, setFiles] = useState<FileMatch[]>([])
  const [highlight, setHighlight] = useState(0)
  const [indexStatus, setIndexStatus] = useState<IndexStatus>('ready')
  const [indexedCount, setIndexedCount] = useState(0)
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const pollRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const lastReq = useRef(0)
  const lastQueryRef = useRef('')
  const containerRef = useRef<HTMLDivElement>(null)
  const keyboardNavRef = useRef(false)
  const contentSearch = useThreadContentSearch(query, excludedKey !== undefined)
  const threads = useMemo(
    () => excludedKey === undefined
      ? []
      : searchThreadMentions(threadList, query, excludedKey.split('\0'), contentSearch.threads),
    [contentSearch.threads, excludedKey, query, threadList]
  )
  const entries = useMemo(
    () => fuzzyRank<MentionEntry>(
      [...files.map((file) => ({ kind: 'file' as const, file })), ...threads.map((thread) => ({ kind: 'thread' as const, thread }))],
      query,
      (entry) => entry.kind === 'file' ? [entry.file.name, entry.file.relativePath] : [threadMentionTitle(entry.thread)]
    ).slice(0, MAX_RESULTS),
    [files, query, threads]
  )
  const entryCount = entries.length

  const clearPoll = useCallback((): void => {
    if (pollRef.current) {
      clearTimeout(pollRef.current)
      pollRef.current = null
    }
  }, [])

  const runSearch = useCallback(
    async (q: string): Promise<void> => {
      const id = ++lastReq.current
      const trimmed = q.trim()
      lastQueryRef.current = q
      setLoading(true)
      try {
        const res = await window.api.workspace.searchFiles({
          query: q,
          workspacePath,
          limit: 10
        })
        if (id !== lastReq.current) return
        const status: IndexStatus = res.indexStatus ?? 'ready'
        // An empty query lists no files but still reports the index status.
        setFiles(trimmed ? (res.files ?? []) : [])
        setIndexStatus(status)
        setIndexedCount(res.indexedCount ?? 0)
        clearPoll()
        if (status === 'building') {
          pollRef.current = setTimeout(() => {
            void runSearch(lastQueryRef.current)
          }, INDEX_POLL_MS)
        }
      } catch {
        if (id !== lastReq.current) return
        setFiles([])
        setIndexStatus('ready')
      } finally {
        if (id === lastReq.current) setLoading(false)
      }
    },
    [clearPoll, workspacePath]
  )

  useEffect(() => {
    if (!visible) {
      setFiles([])
      setHighlight(0)
      clearPoll()
      return
    }
    if (debounceRef.current) clearTimeout(debounceRef.current)
    debounceRef.current = setTimeout(() => {
      void runSearch(query)
    }, DEBOUNCE_MS)
    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current)
    }
  }, [query, visible, runSearch, clearPoll])

  useEffect(() => () => { clearPoll() }, [clearPoll])

  useEffect(() => {
    setHighlight(0)
  }, [entries])

  useEffect(() => {
    if (!visible || !keyboardNavRef.current) return
    keyboardNavRef.current = false
    const active = containerRef.current?.querySelector(`[data-entry-index="${highlight}"]`)
    if (active instanceof HTMLElement && typeof active.scrollIntoView === 'function') {
      active.scrollIntoView({ block: 'nearest' })
    }
  }, [highlight, visible, entries])

  useEffect(() => {
    if (!visible) return
    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') {
        e.preventDefault()
        e.stopPropagation()
        onDismiss()
        return
      }
      if (entryCount === 0) return
      if (e.key === 'ArrowDown') {
        e.preventDefault()
        e.stopPropagation()
        keyboardNavRef.current = true
        setHighlight((h) => Math.min(entryCount - 1, h + 1))
      } else if (e.key === 'ArrowUp') {
        e.preventDefault()
        e.stopPropagation()
        keyboardNavRef.current = true
        setHighlight((h) => Math.max(0, h - 1))
      } else if (e.key === 'Enter' || e.key === 'Tab') {
        e.preventDefault()
        e.stopPropagation()
        const entry = entries[highlight]
        if (entry?.kind === 'thread') threadMentions?.onSelect(entry.thread)
        else if (entry) onSelect(entry.file.relativePath)
      }
    }
    window.addEventListener('keydown', onKey, true)
    return () => { window.removeEventListener('keydown', onKey, true) }
  }, [visible, entryCount, entries, highlight, onSelect, onDismiss, threadMentions])

  if (!visible) return null

  return (
    <MentionPopoverSurface
      popupRef={containerRef}
      open={visible}
      role="listbox"
      maxHeight={260}
    >
      {entryCount === 0 && (loading || (contentSearch.pending && indexStatus !== 'building')) && (
        <div style={mentionEmptyStyle}>{t('fileSearch.loading')}</div>
      )}
      {!loading && entryCount === 0 && indexStatus === 'building' && (
        <IndexingState
          label={indexedCount > 0
            ? t('fileSearch.buildingWithCount', { count: indexedCount })
            : t('fileSearch.building')}
        />
      )}
      {!loading && !contentSearch.pending && entryCount === 0 && indexStatus !== 'building' && query.trim() !== '' && (
        <div style={mentionEmptyStyle}>{t('fileSearch.noMatch')}</div>
      )}
      {!loading && entryCount === 0 && indexStatus !== 'building' && query.trim() === '' && (
        <div style={mentionEmptyStyle}>{t(threadMentions ? 'fileSearch.hintWithChats' : 'fileSearch.hint')}</div>
      )}
      {entries.map((entry, i) => entry.kind === 'thread' ? (
        <button
          key={`thread:${entry.thread.id}`}
          type="button"
          role="option"
          data-entry-index={i}
          aria-selected={i === highlight}
          onMouseEnter={() => { setHighlight(i) }}
          onClick={() => { threadMentions?.onSelect(entry.thread) }}
          className="dotcraft-sidebar-row-radius"
          style={mentionRowStyle(i === highlight)}
        >
          <MentionRowIcon tint="var(--ref-base)">
            <MessagesSquare size={15} strokeWidth={2} aria-hidden />
          </MentionRowIcon>
          <span style={{ ...mentionRowNameStyle, maxWidth: '100%', flexShrink: 1 }}>
            {highlightMatch(threadMentionTitle(entry.thread), query)}
          </span>
        </button>
      ) : (
        <ActionTooltip key={`file:${entry.file.relativePath}`} label={entry.file.relativePath} wrapperStyle={{ display: 'block', width: '100%' }}>
        <button
          type="button"
          role="option"
          data-entry-index={i}
          aria-selected={i === highlight}
          onMouseEnter={() => { setHighlight(i) }}
          onClick={() => { onSelect(entry.file.relativePath) }}
          className="dotcraft-sidebar-row-radius"
          style={mentionRowStyle(i === highlight)}
        >
          <MentionRowIcon>
            <FileTypeIcon path={entry.file.relativePath} size={16} />
          </MentionRowIcon>
          <span style={mentionRowNameStyle}>{highlightMatch(entry.file.name, query)}</span>
          <span style={mentionRowDescStyle}>{entry.file.dir || '.'}</span>
        </button>
        </ActionTooltip>
      ))}
    </MentionPopoverSurface>
  )
}

function IndexingState({ label }: { label: string }): JSX.Element {
  return (
    <div style={indexingContainerStyle}>
      <style>{indexingAnimationCss}</style>
      <div style={indexingLabelStyle}>{label}</div>
      <div
        role="progressbar"
        aria-label={label}
        aria-busy="true"
        style={progressTrackStyle}
      >
        <div style={progressFillStyle} />
      </div>
      <div style={skeletonStackStyle} aria-hidden="true">
        {[0, 1, 2].map((row) => (
          <div
            key={row}
            data-testid="file-search-skeleton-row"
            style={skeletonRowStyle}
          >
            <span style={{ ...skeletonBlockStyle, width: '16px' }} />
            <span style={{ ...skeletonBlockStyle, flex: 1 }} />
            <span style={{ ...skeletonBlockStyle, width: row === 1 ? '88px' : '112px' }} />
          </div>
        ))}
      </div>
    </div>
  )
}

function highlightMatch(name: string, q: string): JSX.Element {
  const lower = name.toLowerCase()
  const qi = q.toLowerCase()
  const idx = lower.indexOf(qi)
  if (!q || idx < 0) return <>{name}</>
  return (
    <>
      {name.slice(0, idx)}
      <span style={{ color: 'var(--accent)' }}>{name.slice(idx, idx + q.length)}</span>
      {name.slice(idx + q.length)}
    </>
  )
}

const indexingAnimationCss = `
@keyframes file-search-progress {
  0% { transform: translateX(-100%); }
  100% { transform: translateX(260%); }
}
@keyframes file-search-skeleton-pulse {
  0%, 100% { opacity: 0.38; }
  50% { opacity: 0.78; }
}
`

const indexingContainerStyle: CSSProperties = {
  padding: '8px 12px 10px',
  display: 'flex',
  flexDirection: 'column',
  gap: '8px'
}

const indexingLabelStyle: CSSProperties = {
  fontSize: '12px',
  color: 'var(--text-dimmed)'
}

const progressTrackStyle: CSSProperties = {
  position: 'relative',
  height: '3px',
  overflow: 'hidden',
  borderRadius: '999px',
  background: 'var(--bg-tertiary)'
}

const progressFillStyle: CSSProperties = {
  position: 'absolute',
  inset: 0,
  width: '38%',
  borderRadius: '999px',
  background: 'var(--accent)',
  animation: 'file-search-progress 1.15s ease-in-out infinite'
}

const skeletonStackStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '6px'
}

const skeletonRowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  height: '18px'
}

const skeletonBlockStyle: CSSProperties = {
  height: '10px',
  borderRadius: '4px',
  background: 'var(--border-default)',
  animation: 'file-search-skeleton-pulse 1.2s ease-in-out infinite'
}
