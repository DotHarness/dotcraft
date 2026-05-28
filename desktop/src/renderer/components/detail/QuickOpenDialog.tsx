/**
 * Quick-Open file finder dialog.
 *
 * UX contract:
 *  - Centered modal with backdrop.
 *  - Focus trapped inside: Tab/Shift+Tab cycles through focusable elements,
 *    Esc closes and returns focus to anchor.
 *  - First open: loads up to 500 workspace files via IPC, then does client-side
 *    fuzzy matching for speed.
 *  - Result items: file icon + file name (match highlighted) + right-aligned relative dir.
 *  - ↑/↓ navigation, Enter to open, Esc to close.
 *  - On file selection: classify → openFile in store → activate viewer tab.
 *
 * References: orca/src/renderer/src/components/QuickOpen.tsx
 */
import {
  useEffect,
  useRef,
  useState,
  useCallback,
  type KeyboardEvent,
  type ChangeEvent
} from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { useConversationStore } from '../../stores/conversationStore'
import { FileText, Search, AlertCircle } from 'lucide-react'

const MAX_FILE_LIST = 500
const MAX_RESULTS = 50

interface FileEntry {
  name: string
  relativePath: string
  dir: string
}

// ─── Fuzzy matching ────────────────────────────────────────────────────────────

interface FuzzyMatch {
  entry: FileEntry
  score: number
  /** Indices of matched characters in the concatenated `name + dir` label. */
  matchedNameIndices: Set<number>
}

function fuzzyMatch(query: string, entries: FileEntry[]): FuzzyMatch[] {
  if (!query.trim()) {
    return entries.slice(0, MAX_RESULTS).map((entry) => ({
      entry,
      score: 0,
      matchedNameIndices: new Set()
    }))
  }

  const q = query.toLowerCase()
  const results: FuzzyMatch[] = []

  for (const entry of entries) {
    const name = entry.name.toLowerCase()
    const rel = entry.relativePath.toLowerCase()

    // Strong bonus for exact basename match
    let score = 0
    let matchedNameIndices = new Set<number>()

    // Try sequential match
    let qi = 0
    const indices: number[] = []
    for (let i = 0; i < rel.length && qi < q.length; i++) {
      if (rel[i] === q[qi]) {
        indices.push(i)
        qi++
      }
    }

    if (qi < q.length) continue // No match

    // Score: consecutive runs are strongly preferred
    let consecutive = 0
    for (let i = 1; i < indices.length; i++) {
      if (indices[i]! === indices[i - 1]! + 1) consecutive++
    }
    score = consecutive * 10 + indices.length

    // Strong bonus for matching at the start of a path segment
    for (const idx of indices) {
      if (idx === 0 || rel[idx - 1] === '/' || rel[idx - 1] === '\\') {
        score += 5
      }
    }

    // Extra bonus if the query is a substring match in the basename
    if (name.includes(q)) {
      score += 20
      // Highlight matched chars in name
      let nameIdx = name.indexOf(q)
      for (let i = nameIdx; i < nameIdx + q.length; i++) matchedNameIndices.add(i)
    }

    results.push({ entry, score, matchedNameIndices })
  }

  // Sort by score descending, then alphabetically
  results.sort((a, b) => b.score - a.score || a.entry.relativePath.localeCompare(b.entry.relativePath))
  return results.slice(0, MAX_RESULTS)
}

// ─── Component ────────────────────────────────────────────────────────────────

interface QuickOpenDialogProps {
  onClose: () => void
  anchorRef?: React.RefObject<HTMLElement | null>
}

type LoadState = 'idle' | 'loading' | 'ok' | 'error'

export function QuickOpenDialog({ onClose, anchorRef }: QuickOpenDialogProps): JSX.Element {
  const t = useT()
  const setQuickOpenVisible = useUIStore((s) => s.setQuickOpenVisible)
  const setActiveViewerTab = useUIStore((s) => s.setActiveViewerTab)
  const setDetailPanelVisible = useUIStore((s) => s.setDetailPanelVisible)

  const currentThreadId = useViewerTabStore((s) => s.currentThreadId)
  const openFile = useViewerTabStore((s) => s.openFile)

  const workspacePath = useConversationStore((s) => s.workspacePath)

  const [query, setQuery] = useState('')
  const [loadState, setLoadState] = useState<LoadState>('idle')
  const [allFiles, setAllFiles] = useState<FileEntry[]>([])
  const [results, setResults] = useState<FuzzyMatch[]>([])
  const [selectedIdx, setSelectedIdx] = useState(0)
  const [classifyError, setClassifyError] = useState<string | null>(null)

  const inputRef = useRef<HTMLInputElement>(null)
  const listRef = useRef<HTMLUListElement>(null)
  const dialogRef = useRef<HTMLDivElement>(null)
  const indexPollTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const loadFilesRef = useRef<(() => void) | null>(null)

  // Load file list on mount
  const loadFiles = useCallback(async (): Promise<void> => {
    if (!workspacePath) return
    if (indexPollTimerRef.current) {
      clearTimeout(indexPollTimerRef.current)
      indexPollTimerRef.current = null
    }
    setLoadState('loading')
    setClassifyError(null)
    try {
      const response = await window.api.workspace.viewer.listFiles({
        workspacePath,
        query: '',
        limit: MAX_FILE_LIST
      })
      const files = response.files
      setAllFiles(files)
      setResults(fuzzyMatch('', files))
      if (response.indexStatus === 'building' && files.length === 0) {
        setLoadState('loading')
        indexPollTimerRef.current = setTimeout(() => {
          loadFilesRef.current?.()
        }, 1500)
      } else {
        setLoadState('ok')
        if (response.indexStatus === 'building') {
          indexPollTimerRef.current = setTimeout(() => {
            loadFilesRef.current?.()
          }, 2500)
        }
      }
    } catch {
      setLoadState('error')
    }
  }, [workspacePath])
  loadFilesRef.current = () => {
    void loadFiles()
  }

  useEffect(() => {
    void loadFiles()
    return () => {
      if (indexPollTimerRef.current) {
        clearTimeout(indexPollTimerRef.current)
        indexPollTimerRef.current = null
      }
    }
  }, [loadFiles])

  // Focus input on open
  useEffect(() => {
    inputRef.current?.focus()
  }, [])

  // Update results when query changes
  useEffect(() => {
    setResults(fuzzyMatch(query, allFiles))
    setSelectedIdx(0)
    setClassifyError(null)
  }, [query, allFiles])

  // Scroll selected item into view
  useEffect(() => {
    const el = listRef.current?.children[selectedIdx] as HTMLElement | undefined
    el?.scrollIntoView({ block: 'nearest' })
  }, [selectedIdx])

  const close = useCallback((): void => {
    setQuickOpenVisible(false)
    onClose()
    anchorRef?.current?.focus()
  }, [anchorRef, onClose, setQuickOpenVisible])

  const openSelected = useCallback(async (overrideIdx?: number): Promise<void> => {
    const idx = overrideIdx ?? selectedIdx
    const match = results[idx]
    if (!match || !workspacePath || !currentThreadId) return

    const absolutePath = `${workspacePath.replace(/\\/g, '/')}/${match.entry.relativePath}`

    setClassifyError(null)
    try {
      const classified = await window.api.workspace.viewer.classify({ absolutePath })
      const tabId = openFile({
        threadId: currentThreadId,
        absolutePath,
        relativePath: match.entry.relativePath,
        contentClass: classified.contentClass,
        sizeBytes: classified.sizeBytes
      })
      setActiveViewerTab(tabId)
      setDetailPanelVisible(true)
      close()
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err)
      setClassifyError(msg)
    }
  }, [close, currentThreadId, openFile, results, selectedIdx, setActiveViewerTab, setDetailPanelVisible, workspacePath])

  const handleKeyDown = (e: KeyboardEvent<HTMLInputElement>): void => {
    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault()
        setSelectedIdx((i) => Math.min(i + 1, results.length - 1))
        break
      case 'ArrowUp':
        e.preventDefault()
        setSelectedIdx((i) => Math.max(i - 1, 0))
        break
      case 'Enter':
        e.preventDefault()
        void openSelected()
        break
      case 'Escape':
        e.preventDefault()
        close()
        break
    }
  }

  const handleInputChange = (e: ChangeEvent<HTMLInputElement>): void => {
    setQuery(e.target.value)
  }

  // Backdrop click to close
  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>): void => {
    if (e.target === e.currentTarget) close()
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={t('quickOpen.title')}
      onClick={handleBackdropClick}
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 2000,
        display: 'flex',
        alignItems: 'flex-start',
        justifyContent: 'center',
        paddingTop: '12vh',
        backgroundColor: 'rgba(0,0,0,0.5)'
      }}
    >
      <div
        ref={dialogRef}
        style={{
          width: '560px',
          maxWidth: 'calc(100vw - 48px)',
          backgroundColor: 'var(--bg-elevated, #1e1e1e)',
          border: '1px solid var(--border-default)',
          borderRadius: '8px',
          boxShadow: '0 8px 32px rgba(0,0,0,0.4)',
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column'
        }}
      >
        {/* Search input */}
        <div style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          padding: '10px 12px',
          borderBottom: '1px solid var(--border-default)'
        }}>
          <Search size={16} aria-hidden style={{ color: 'var(--text-secondary)', flexShrink: 0 }} />
          <input
            ref={inputRef}
            type="text"
            value={query}
            onChange={handleInputChange}
            onKeyDown={handleKeyDown}
            placeholder={t('quickOpen.placeholder')}
            aria-label={t('quickOpen.placeholder')}
            aria-autocomplete="list"
            aria-controls="quick-open-list"
            style={{
              flex: 1,
              border: 'none',
              outline: 'none',
              background: 'transparent',
              color: 'var(--text-primary)',
              fontSize: '14px',
              caretColor: 'var(--accent)'
            }}
          />
        </div>

        {/* Results */}
        <div style={{ maxHeight: '320px', overflowY: 'auto' }}>
          {loadState === 'loading' && (
            <div style={{ padding: '16px 12px', color: 'var(--text-secondary)', fontSize: '13px' }}>
              {t('quickOpen.loading')}
            </div>
          )}

          {loadState === 'error' && (
            <div style={{
              padding: '16px 12px',
              display: 'flex',
              gap: '8px',
              alignItems: 'center',
              color: 'var(--text-secondary)',
              fontSize: '13px'
            }}>
              <AlertCircle size={14} aria-hidden />
              <span>{t('quickOpen.retry')}</span>
              <button
                onClick={() => void loadFiles()}
                style={{
                  background: 'transparent',
                  border: '1px solid var(--border-default)',
                  color: 'var(--text-secondary)',
                  padding: '2px 8px',
                  borderRadius: '4px',
                  cursor: 'pointer',
                  fontSize: '12px'
                }}
              >
                {t('common.retry')}
              </button>
            </div>
          )}

          {loadState === 'ok' && results.length === 0 && (
            <div style={{ padding: '16px 12px', color: 'var(--text-secondary)', fontSize: '13px' }}>
              {t('quickOpen.noMatch')}
            </div>
          )}

          {loadState === 'ok' && results.length > 0 && (
            <ul
              id="quick-open-list"
              role="listbox"
              ref={listRef}
              style={{ listStyle: 'none', margin: 0, padding: '4px 0' }}
            >
              {results.map((match, idx) => {
                const isSelected = idx === selectedIdx
                const { name, dir } = match.entry
                return (
                  <li
                    key={match.entry.relativePath}
                    role="option"
                    aria-selected={isSelected}
                    onClick={() => {
                      setSelectedIdx(idx)
                      void openSelected(idx)
                    }}
                    onMouseEnter={() => setSelectedIdx(idx)}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: '8px',
                      padding: '5px 12px',
                      cursor: 'pointer',
                      backgroundColor: isSelected ? 'var(--bg-selected, rgba(255,255,255,0.1))' : 'transparent',
                      fontSize: '13px',
                      color: 'var(--text-primary)'
                    }}
                  >
                    <FileText size={14} aria-hidden style={{ flexShrink: 0, color: 'var(--text-secondary)' }} />
                    <span style={{
                      flex: 1,
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap'
                    }}>
                      <HighlightedText text={name} matchedIndices={match.matchedNameIndices} />
                    </span>
                    {dir && (
                      <span style={{
                        flexShrink: 0,
                        color: 'var(--text-secondary)',
                        fontSize: '11px',
                        opacity: 0.7,
                        maxWidth: '160px',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap'
                      }}>
                        {dir}
                      </span>
                    )}
                  </li>
                )
              })}
            </ul>
          )}
        </div>

        {/* Error notice */}
        {classifyError && (
          <div style={{
            padding: '8px 12px',
            borderTop: '1px solid var(--border-default)',
            color: 'var(--text-error, #e05c5c)',
            fontSize: '12px'
          }}>
            {classifyError}
          </div>
        )}
      </div>
    </div>
  )
}

function HighlightedText({
  text,
  matchedIndices
}: {
  text: string
  matchedIndices: Set<number>
}): JSX.Element {
  if (matchedIndices.size === 0) return <>{text}</>

  const parts: JSX.Element[] = []
  let i = 0
  while (i < text.length) {
    if (matchedIndices.has(i)) {
      let j = i
      while (j < text.length && matchedIndices.has(j)) j++
      parts.push(
        <mark
          key={i}
          style={{
            backgroundColor: 'transparent',
            color: 'var(--accent, #4a90ff)',
            fontWeight: 600
          }}
        >
          {text.slice(i, j)}
        </mark>
      )
      i = j
    } else {
      let j = i
      while (j < text.length && !matchedIndices.has(j)) j++
      parts.push(<span key={i}>{text.slice(i, j)}</span>)
      i = j
    }
  }
  return <>{parts}</>
}
