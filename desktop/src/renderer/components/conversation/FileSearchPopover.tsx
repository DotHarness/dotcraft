import { useCallback, useEffect, useRef, useState, type CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'
import { FileTypeIcon } from '../ui/FileTypeIcon'
import {
  MentionRowIcon,
  MentionSectionHeader,
  mentionEmptyStyle,
  MentionPopoverSurface,
  mentionRowDescStyle,
  mentionRowNameStyle,
  mentionRowStyle
} from './mentionPopoverUi'

const DEBOUNCE_MS = 80
const INDEX_POLL_MS = 1500

export interface FileMatch {
  name: string
  relativePath: string
  dir: string
}

type IndexStatus = 'empty' | 'building' | 'ready'

interface FileSearchPopoverProps {
  query: string
  visible: boolean
  workspacePath: string
  onSelect: (relativePath: string) => void
  onDismiss: () => void
  constrainToAnchor?: boolean
}

/**
 * Floating file search for @ mentions; debounced IPC to workspace.searchFiles.
 * When the workspace index is still being built (typical for very large
 * projects on first open), the popover surfaces "Indexing files…" and polls
 * the IPC every {@link INDEX_POLL_MS} until the index reaches `ready`, so the
 * user does not have to close and reopen the @ trigger to see results.
 */
export function FileSearchPopover({
  query,
  visible,
  workspacePath,
  onSelect,
  onDismiss,
  constrainToAnchor = false
}: FileSearchPopoverProps): JSX.Element | null {
  const t = useT()
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
        // Empty query never shows arbitrary files; we still issue the IPC so
        // the popover knows when the index is still being built.
        setFiles(trimmed ? (res.files ?? []) : [])
        setIndexStatus(status)
        setIndexedCount(res.indexedCount ?? 0)
        clearPoll()
        if (status === 'building') {
          // Index is still being built; auto-retry so the popover refreshes
          // as soon as new entries are available without user action.
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
  }, [files])

  // Keep the highlighted row scrolled into view as Arrow keys move past the
  // scrollable container's edge (see CommandSearchPopover for the same pattern).
  useEffect(() => {
    if (!visible) return
    const active = containerRef.current?.querySelector(`[data-entry-index="${highlight}"]`)
    if (active instanceof HTMLElement && typeof active.scrollIntoView === 'function') {
      active.scrollIntoView({ block: 'nearest' })
    }
  }, [highlight, visible, files])

  useEffect(() => {
    if (!visible) return
    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') {
        e.preventDefault()
        e.stopPropagation()
        onDismiss()
        return
      }
      if (files.length === 0) return
      if (e.key === 'ArrowDown') {
        e.preventDefault()
        e.stopPropagation()
        setHighlight((h) => Math.min(files.length - 1, h + 1))
      } else if (e.key === 'ArrowUp') {
        e.preventDefault()
        e.stopPropagation()
        setHighlight((h) => Math.max(0, h - 1))
      } else if (e.key === 'Enter' || e.key === 'Tab') {
        e.preventDefault()
        e.stopPropagation()
        const f = files[highlight]
        if (f) onSelect(f.relativePath)
      }
    }
    window.addEventListener('keydown', onKey, true)
    return () => { window.removeEventListener('keydown', onKey, true) }
  }, [visible, files, highlight, onSelect, onDismiss])

  if (!visible) return null

  return (
    <MentionPopoverSurface
      popupRef={containerRef}
      open={visible}
      role="listbox"
      constrainToAnchor={constrainToAnchor}
      minWidth="300px"
      maxWidth="440px"
      maxHeight="260px"
    >
      {loading && files.length === 0 && (
        <div style={mentionEmptyStyle}>{t('fileSearch.loading')}</div>
      )}
      {!loading && files.length === 0 && indexStatus === 'building' && (
        <IndexingState
          label={indexedCount > 0
            ? t('fileSearch.buildingWithCount', { count: indexedCount })
            : t('fileSearch.building')}
        />
      )}
      {!loading && files.length === 0 && indexStatus !== 'building' && query.trim() !== '' && (
        <div style={mentionEmptyStyle}>{t('fileSearch.noMatch')}</div>
      )}
      {!loading && files.length === 0 && indexStatus !== 'building' && query.trim() === '' && (
        <div style={mentionEmptyStyle}>{t('fileSearch.hint')}</div>
      )}
      {files.length > 0 && <MentionSectionHeader label={t('fileSearch.filesGroup')} />}
      {files.map((f, i) => {
        const dirLabel = f.dir || '.'
        return (
        <ActionTooltip key={f.relativePath} label={f.relativePath} wrapperStyle={{ display: 'block', width: '100%' }}>
        <button
          type="button"
          role="option"
          data-entry-index={i}
          aria-selected={i === highlight}
          onMouseEnter={() => { setHighlight(i) }}
          onClick={() => { onSelect(f.relativePath) }}
          style={mentionRowStyle(i === highlight)}
        >
          <MentionRowIcon>
            <FileTypeIcon path={f.relativePath} size={16} />
          </MentionRowIcon>
          <span style={mentionRowNameStyle}>{highlightMatch(f.name, query)}</span>
          <span style={mentionRowDescStyle}>{dirLabel}</span>
        </button>
        </ActionTooltip>
        )
      })}
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
