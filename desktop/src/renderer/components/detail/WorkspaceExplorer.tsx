/**
 * Built-in workspace resource browser, docked as a resizable sub-panel on the
 * right of the file viewer.
 *
 *  - Lazy tree: a folder's children are loaded on first expand via
 *    `workspace:viewer:list-dir` (NOT gitignore-filtered, so build/cache dirs
 *    stay browsable).
 *  - Click a file  → classify + open a viewer tab (same flow as Quick-Open).
 *  - Right-click   → the shared chat file-pill menu (`ReferencePathContextMenu`).
 *  - Filter box    → case-insensitive name filter over the already-loaded tree.
 */
import {
  Fragment,
  useCallback,
  useEffect,
  useRef,
  useState,
  type CSSProperties
} from 'react'
import { ChevronDown, ChevronRight, Search } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { useUIStore } from '../../stores/uiStore'
import { addToast } from '../../stores/toastStore'
import { FileTypeIcon } from '../ui/FileTypeIcon'
import { Skeleton } from '../ui/Skeleton'
import { ReferencePathContextMenu } from '../conversation/ReferencePathContextMenu'
import type { ContextMenuPosition } from '../ui/ContextMenu'
import type { DirEntryWire } from '../../../shared/viewer/types'

const norm = (p: string): string => p.replace(/\\/g, '/')

export function WorkspaceExplorer(): JSX.Element {
  const t = useT()
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const currentThreadId = useViewerTabStore((s) => s.currentThreadId)
  const openFile = useViewerTabStore((s) => s.openFile)
  const setActiveViewerTab = useUIStore((s) => s.setActiveViewerTab)
  const explorerRevealPath = useUIStore((s) => s.explorerRevealPath)
  const consumeExplorerReveal = useUIStore((s) => s.consumeExplorerReveal)

  const rootKey = workspacePath ? norm(workspacePath).replace(/\/+$/, '') : ''

  const [childrenCache, setChildrenCache] = useState<Map<string, DirEntryWire[]>>(new Map())
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [errored, setErrored] = useState<Set<string>>(new Set())
  const [filter, setFilter] = useState('')
  const [contextMenu, setContextMenu] = useState<{ position: ContextMenuPosition; targetPath: string } | null>(null)
  const [scrollTargetKey, setScrollTargetKey] = useState<string | null>(null)

  const loadingRef = useRef<Set<string>>(new Set())
  const scrollRowRef = useRef<HTMLDivElement>(null)

  const loadDir = useCallback(async (absDir: string): Promise<void> => {
    const key = norm(absDir).replace(/\/+$/, '')
    if (loadingRef.current.has(key)) return
    loadingRef.current.add(key)
    try {
      const res = await window.api.workspace.viewer.listDir({ dirPath: absDir })
      setChildrenCache((prev) => new Map(prev).set(key, res.entries))
      setErrored((prev) => {
        if (!prev.has(key)) return prev
        const next = new Set(prev)
        next.delete(key)
        return next
      })
    } catch {
      setErrored((prev) => new Set(prev).add(key))
    } finally {
      loadingRef.current.delete(key)
    }
  }, [])

  // Reset and load the root whenever the workspace changes.
  useEffect(() => {
    setChildrenCache(new Map())
    setExpanded(new Set())
    setErrored(new Set())
    loadingRef.current = new Set()
    if (rootKey) void loadDir(rootKey)
  }, [rootKey, loadDir])

  // Expand ancestors and scroll to a folder requested via a breadcrumb click.
  useEffect(() => {
    if (!explorerRevealPath || !rootKey) return
    const targetFwd = norm(explorerRevealPath).replace(/\/+$/, '')
    consumeExplorerReveal()
    if (!targetFwd.startsWith(rootKey)) return
    const parts = targetFwd.slice(rootKey.length).split('/').filter(Boolean)
    let cancelled = false
    void (async () => {
      const toExpand: string[] = []
      for (let i = 0; i < parts.length; i++) {
        const dirAbs = `${rootKey}/${parts.slice(0, i + 1).join('/')}`
        toExpand.push(dirAbs)
        await loadDir(dirAbs)
        if (cancelled) return
      }
      setExpanded((prev) => new Set([...prev, ...toExpand]))
      setScrollTargetKey(targetFwd)
    })()
    return () => { cancelled = true }
  }, [explorerRevealPath, rootKey, loadDir, consumeExplorerReveal])

  // Scroll the revealed row into view once it has rendered.
  useEffect(() => {
    if (scrollTargetKey && scrollRowRef.current) {
      scrollRowRef.current.scrollIntoView({ block: 'center' })
      setScrollTargetKey(null)
    }
  }, [scrollTargetKey, childrenCache, expanded])

  const toggleDir = useCallback((absDir: string): void => {
    const key = norm(absDir).replace(/\/+$/, '')
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(key)) {
        next.delete(key)
      } else {
        next.add(key)
        void loadDir(absDir)
      }
      return next
    })
  }, [loadDir])

  const openFileEntry = useCallback(async (entry: DirEntryWire): Promise<void> => {
    if (!currentThreadId) return
    try {
      const classified = await window.api.workspace.viewer.classify({ absolutePath: entry.absolutePath })
      const tabId = openFile({
        threadId: currentThreadId,
        absolutePath: entry.absolutePath,
        relativePath: entry.relativePath,
        contentClass: classified.contentClass,
        sizeBytes: classified.sizeBytes
      })
      setActiveViewerTab(tabId)
    } catch {
      addToast(t('viewer.readFailed'), 'warning')
    }
  }, [currentThreadId, openFile, setActiveViewerTab, t])

  const q = filter.trim().toLowerCase()

  const subtreeMatches = useCallback((absKey: string, query: string): boolean => {
    const kids = childrenCache.get(absKey)
    if (!kids) return false
    for (const kid of kids) {
      if (kid.name.toLowerCase().includes(query)) return true
      if (kid.isDir && subtreeMatches(norm(kid.absolutePath).replace(/\/+$/, ''), query)) return true
    }
    return false
  }, [childrenCache])

  const renderChildren = (dirKey: string, depth: number): JSX.Element => {
    const kids = childrenCache.get(dirKey)
    if (kids === undefined) {
      return errored.has(dirKey)
        ? <Placeholder depth={depth} text={t('viewer.explorerLoadFailed')} />
        : <TreeSkeleton depth={depth} ariaLabel={t('quickOpen.loading')} />
    }
    const visible = q
      ? kids.filter((k) => k.name.toLowerCase().includes(q) || (k.isDir && subtreeMatches(norm(k.absolutePath).replace(/\/+$/, ''), q)))
      : kids
    if (visible.length === 0) {
      return <Placeholder depth={depth} text={q ? t('viewer.explorerNoMatch') : t('viewer.explorerEmpty')} />
    }
    return <>{visible.map((entry) => renderNode(entry, depth))}</>
  }

  const renderNode = (entry: DirEntryWire, depth: number): JSX.Element => {
    const key = norm(entry.absolutePath).replace(/\/+$/, '')
    const isOpen = entry.isDir && (expanded.has(key) || (q !== '' && subtreeMatches(key, q)))
    const isScrollTarget = scrollTargetKey === key
    return (
      <Fragment key={key}>
        <div
          ref={isScrollTarget ? scrollRowRef : undefined}
          role="treeitem"
          aria-expanded={entry.isDir ? isOpen : undefined}
          title={entry.relativePath}
          onClick={() => { entry.isDir ? toggleDir(entry.absolutePath) : void openFileEntry(entry) }}
          onContextMenu={(event) => {
            event.preventDefault()
            event.stopPropagation()
            setContextMenu({ position: { x: event.clientX, y: event.clientY }, targetPath: entry.absolutePath })
          }}
          style={{ ...rowStyle, paddingLeft: 8 + depth * 14 }}
          onMouseEnter={(e) => { (e.currentTarget as HTMLDivElement).style.background = 'var(--bg-tertiary)' }}
          onMouseLeave={(e) => { (e.currentTarget as HTMLDivElement).style.background = 'transparent' }}
        >
          <span style={chevronSlotStyle}>
            {entry.isDir && (
              isOpen
                ? <ChevronDown size={13} aria-hidden style={{ color: 'var(--text-secondary)' }} />
                : <ChevronRight size={13} aria-hidden style={{ color: 'var(--text-secondary)' }} />
            )}
          </span>
          <FileTypeIcon path={entry.name} size={15} dir={entry.isDir} expanded={isOpen} />
          <span style={rowLabelStyle}>{entry.name}</span>
        </div>
        {isOpen && renderChildren(key, depth + 1)}
      </Fragment>
    )
  }

  return (
    <div style={panelStyle}>
      <div style={toolbarStyle}>
        <div style={searchWrapStyle}>
          <Search size={13} aria-hidden style={{ color: 'var(--text-secondary)', flexShrink: 0 }} />
          <input
            type="text"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder={t('viewer.explorerFilter')}
            aria-label={t('viewer.explorerFilter')}
            style={searchInputStyle}
          />
        </div>
      </div>

      <div role="tree" aria-label={t('viewer.explorerTitle')} style={treeStyle}>
        {!rootKey
          ? <Placeholder depth={0} text={t('viewer.explorerNoWorkspace')} />
          : renderChildren(rootKey, 0)}
      </div>

      {contextMenu && (
        <ReferencePathContextMenu
          position={contextMenu.position}
          targetPath={contextMenu.targetPath}
          onClose={() => setContextMenu(null)}
        />
      )}
    </div>
  )
}

function TreeSkeleton({ depth, ariaLabel }: { depth: number; ariaLabel: string }): JSX.Element {
  return (
    <div role="status" aria-busy="true" aria-label={ariaLabel}>
      {[64, 48, 56].map((width, index) => (
        <div
          key={index}
          aria-hidden="true"
          style={{ ...rowStyle, cursor: 'default', paddingLeft: 8 + depth * 14 }}
        >
          <span style={chevronSlotStyle} />
          <Skeleton width={15} height={15} radius={4} />
          <Skeleton width={`${width}%`} height={11} />
        </div>
      ))}
    </div>
  )
}

function Placeholder({ depth, text }: { depth: number; text: string }): JSX.Element {
  return (
    <div style={{ ...placeholderStyle, paddingLeft: 8 + depth * 14 + 17 }}>
      {text}
    </div>
  )
}

const panelStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  height: '100%',
  minWidth: 0,
  background: 'var(--bg-secondary)'
}

const toolbarStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '6px',
  height: '38px',
  flexShrink: 0,
  padding: '0 6px 0 8px',
  boxSizing: 'border-box',
  borderBottom: '1px solid var(--glass-border)'
}

const searchWrapStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '6px',
  flex: 1,
  minWidth: 0,
  height: '26px',
  padding: '0 8px',
  borderRadius: '6px',
  border: '1px solid var(--border-default)',
  background: 'var(--bg-primary)'
}

const searchInputStyle: CSSProperties = {
  flex: 1,
  minWidth: 0,
  border: 'none',
  outline: 'none',
  background: 'transparent',
  color: 'var(--text-primary)',
  fontSize: '12px',
  caretColor: 'var(--accent)'
}

const treeStyle: CSSProperties = {
  flex: 1,
  overflowY: 'auto',
  overflowX: 'hidden',
  padding: '4px 0'
}

const rowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '5px',
  height: '24px',
  paddingRight: '8px',
  cursor: 'pointer',
  fontSize: '13px',
  color: 'var(--text-primary)',
  userSelect: 'none',
  transition: 'background-color 80ms ease'
}

const chevronSlotStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  width: '14px',
  flexShrink: 0
}

const rowLabelStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const placeholderStyle: CSSProperties = {
  padding: '4px 8px',
  fontSize: '12px',
  color: 'var(--text-secondary)',
  whiteSpace: 'nowrap',
  overflow: 'hidden',
  textOverflow: 'ellipsis'
}
