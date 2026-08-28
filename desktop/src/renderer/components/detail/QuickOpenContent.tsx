/**
 * Presentation-agnostic so it can be hosted as a centered modal (`QuickOpenDialog`)
 * or an anchored dropdown (`JumpToFileButton`). First mount loads up to 500
 * workspace files over IPC; matching is client-side from then on.
 */
import {
  useEffect,
  useRef,
  useState,
  useCallback,
  type CSSProperties,
  type KeyboardEvent,
  type ChangeEvent
} from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { useConversationStore } from '../../stores/conversationStore'
import { Search, AlertCircle } from 'lucide-react'
import { FileTypeIcon } from '../ui/FileTypeIcon'
import { Input } from '../ui/Input'

const MAX_FILE_LIST = 500
const MAX_RESULTS = 50

interface FileEntry {
  name: string
  relativePath: string
  dir: string
}

interface FuzzyMatch {
  entry: FileEntry
  score: number
  /** Indices of matched characters in the file name label. */
  matchedNameIndices: Set<number>
}

export function fuzzyMatch(query: string, entries: FileEntry[]): FuzzyMatch[] {
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

    let score = 0
    const matchedNameIndices = new Set<number>()

    let qi = 0
    const indices: number[] = []
    for (let i = 0; i < rel.length && qi < q.length; i++) {
      if (rel[i] === q[qi]) {
        indices.push(i)
        qi++
      }
    }

    if (qi < q.length) continue

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

    if (name.includes(q)) {
      score += 20
      const nameIdx = name.indexOf(q)
      for (let i = nameIdx; i < nameIdx + q.length; i++) matchedNameIndices.add(i)
    }

    results.push({ entry, score, matchedNameIndices })
  }

  results.sort((a, b) => b.score - a.score || a.entry.relativePath.localeCompare(b.entry.relativePath))
  return results.slice(0, MAX_RESULTS)
}

type LoadState = 'idle' | 'loading' | 'ok' | 'error'

interface QuickOpenContentProps {
  /** Called after a file opens, on Escape, or when the finder should dismiss. */
  onClose: () => void
  /** Search input placeholder + aria-label. Defaults to `quickOpen.placeholder`. */
  placeholder?: string
  /** Max height of the scrollable result list. */
  resultsMaxHeight?: number
}

export function QuickOpenContent({
  onClose,
  placeholder,
  resultsMaxHeight = 320
}: QuickOpenContentProps): JSX.Element {
  const t = useT()
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
  const indexPollTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const loadFilesRef = useRef<(() => void) | null>(null)

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

  useEffect(() => {
    inputRef.current?.focus()
  }, [])

  useEffect(() => {
    setResults(fuzzyMatch(query, allFiles))
    setSelectedIdx(0)
    setClassifyError(null)
  }, [query, allFiles])

  useEffect(() => {
    const el = listRef.current?.children[selectedIdx] as HTMLElement | undefined
    el?.scrollIntoView({ block: 'nearest' })
  }, [selectedIdx])

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
      onClose()
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err)
      setClassifyError(msg)
    }
  }, [currentThreadId, onClose, openFile, results, selectedIdx, setActiveViewerTab, setDetailPanelVisible, workspacePath])

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
        onClose()
        break
    }
  }

  const handleInputChange = (e: ChangeEvent<HTMLInputElement>): void => {
    setQuery(e.target.value)
  }

  const inputLabel = placeholder ?? t('quickOpen.placeholder')

  return (
    <>
      <div style={searchRowStyle}>
        <Search size={16} aria-hidden style={{ color: 'var(--text-secondary)', flexShrink: 0 }} />
        <Input
          ref={inputRef}
          value={query}
          onChange={handleInputChange}
          onKeyDown={handleKeyDown}
          placeholder={inputLabel}
          aria-label={inputLabel}
          aria-autocomplete="list"
          aria-controls="quick-open-list"
          bare
          style={{ flex: 1, minWidth: 0, fontSize: '14px', caretColor: 'var(--accent)' }}
        />
      </div>

      <div style={{ maxHeight: `${resultsMaxHeight}px`, overflowY: 'auto' }}>
        {loadState === 'loading' && (
          <div style={statusStyle}>{t('quickOpen.loading')}</div>
        )}

        {loadState === 'error' && (
          <div style={{ ...statusStyle, display: 'flex', gap: '8px', alignItems: 'center' }}>
            <AlertCircle size={14} aria-hidden />
            <span>{t('quickOpen.retry')}</span>
            <button onClick={() => void loadFiles()} style={retryButtonStyle}>
              {t('common.retry')}
            </button>
          </div>
        )}

        {loadState === 'ok' && results.length === 0 && (
          <div style={statusStyle}>{t('quickOpen.noMatch')}</div>
        )}

        {loadState === 'ok' && results.length > 0 && (
          <ul id="quick-open-list" role="listbox" ref={listRef} style={listStyle}>
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
                    ...rowStyle,
                    backgroundColor: isSelected ? 'var(--sidebar-control-hover)' : 'transparent'
                  }}
                >
                  <FileTypeIcon path={match.entry.relativePath} size={14} style={{ flexShrink: 0 }} />
                  <span style={rowNameStyle}>
                    <HighlightedText text={name} matchedIndices={match.matchedNameIndices} />
                  </span>
                  {dir && <span style={rowDirStyle}>{dir}</span>}
                </li>
              )
            })}
          </ul>
        )}
      </div>

      {classifyError && <div style={errorStyle}>{classifyError}</div>}
    </>
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
        <mark key={i} style={highlightStyle}>
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

const searchRowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  padding: '10px 12px',
  borderBottom: '1px solid var(--glass-border)'
}

const statusStyle: CSSProperties = {
  padding: '16px 12px',
  color: 'var(--text-secondary)',
  fontSize: '13px'
}

const retryButtonStyle: CSSProperties = {
  background: 'transparent',
  border: '1px solid var(--border-default)',
  color: 'var(--text-secondary)',
  padding: '2px 8px',
  borderRadius: '4px',
  cursor: 'pointer',
  fontSize: '12px'
}

const listStyle: CSSProperties = {
  listStyle: 'none',
  margin: 0,
  padding: '6px 0'
}

const rowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  margin: '0 6px',
  padding: '6px 8px',
  borderRadius: '6px',
  cursor: 'pointer',
  fontSize: '13px',
  color: 'var(--text-primary)'
}

const rowNameStyle: CSSProperties = {
  flex: 1,
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const rowDirStyle: CSSProperties = {
  flexShrink: 0,
  color: 'var(--text-secondary)',
  fontSize: '11px',
  opacity: 0.7,
  maxWidth: '180px',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const highlightStyle: CSSProperties = {
  backgroundColor: 'transparent',
  color: 'var(--accent, #4a90ff)',
  fontWeight: 600
}

const errorStyle: CSSProperties = {
  padding: '8px 12px',
  borderTop: '1px solid var(--glass-border)',
  color: 'var(--text-error, #e05c5c)',
  fontSize: '12px'
}
