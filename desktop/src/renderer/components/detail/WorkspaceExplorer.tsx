/**
 * A folder's children load on first expand via `workspace:viewer:list-dir`, which is
 * deliberately not gitignore-filtered so build and cache dirs stay browsable. The
 * filter box only searches the tree already loaded.
 */
import { Fragment, useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { Search } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { Input } from '../ui/Input'
import { useConversationStore } from '../../stores/conversationStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { useUIStore } from '../../stores/uiStore'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ReferencePathContextMenu } from '../conversation/ReferencePathContextMenu'
import type { ContextMenuPosition } from '../ui/ContextMenu'
import type { DirEntryWire } from '../../../shared/viewer/types'
import { containingRoot, viewerRootsFor } from '../../utils/viewerRoots'
import { ExplorerRootPicker } from './ExplorerRootPicker'
import { DirTreePlaceholder, DirTreeRow, DirTreeSkeleton, dirKey as norm, useDirTree, useOpenDirEntry } from './DirTree'

export function WorkspaceExplorer(): JSX.Element {
  const t = useT()
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const projects = useWorkspaceProjectsStore((s) => s.projects)
  const activeFilePath = useViewerTabStore((s) => {
    if (!s.currentThreadId) return null
    const state = s.getThreadState(s.currentThreadId)
    const active = state.tabs.find((tab) => tab.id === state.activeTabId)
    return active?.kind === 'file' ? active.absolutePath : null
  })
  const explorerRevealPath = useUIStore((s) => s.explorerRevealPath)
  const consumeExplorerReveal = useUIStore((s) => s.consumeExplorerReveal)

  const roots = useMemo(() => viewerRootsFor(workspacePath), [workspacePath, projects])
  const [selectedRoot, setSelectedRoot] = useState<string | null>(null)
  const root = selectedRoot && roots.includes(selectedRoot)
    ? selectedRoot
    : (activeFilePath ? containingRoot(activeFilePath, roots) : null) ?? roots[0] ?? ''
  const rootKey = root ? norm(root) : ''

  const followedFileRef = useRef<string | null>(null)
  useEffect(() => {
    if (!activeFilePath || followedFileRef.current === activeFilePath) return
    followedFileRef.current = activeFilePath
    if (root && containingRoot(activeFilePath, [root])) return
    const next = containingRoot(activeFilePath, roots)
    if (next) setSelectedRoot(next)
  }, [activeFilePath, roots, root])

  useEffect(() => { setSelectedRoot(null) }, [workspacePath])

  const { childrenCache, expanded, errored, loadDir, toggleDir, expandDirs, reset } = useDirTree()
  const openFileEntry = useOpenDirEntry()
  const [filter, setFilter] = useState('')
  const [contextMenu, setContextMenu] = useState<{
    position: ContextMenuPosition
    targetPath: string
    isDirectory: boolean
  } | null>(null)
  const [scrollTargetKey, setScrollTargetKey] = useState<string | null>(null)

  const scrollRowRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    reset()
    setFilter('')
    if (rootKey) void loadDir(rootKey)
  }, [rootKey, loadDir, reset])

  useEffect(() => {
    if (!explorerRevealPath || !rootKey) return
    const targetRoot = containingRoot(explorerRevealPath, roots)
    if (!targetRoot) {
      consumeExplorerReveal()
      return
    }
    if (norm(targetRoot) !== rootKey) {
      setSelectedRoot(targetRoot)
      return
    }
    consumeExplorerReveal()
    const target = norm(explorerRevealPath)
    const parts = target.length > rootKey.length
      ? target.slice(rootKey.length + 1).split('/').filter(Boolean)
      : []
    const targetKey = parts.length === 0 ? rootKey : `${rootKey}/${parts.join('/')}`
    let cancelled = false
    void (async () => {
      const toExpand: string[] = []
      for (let i = 0; i < parts.length; i++) {
        const dirAbs = `${rootKey}/${parts.slice(0, i + 1).join('/')}`
        toExpand.push(dirAbs)
        await loadDir(dirAbs)
        if (cancelled) return
      }
      expandDirs(toExpand)
      setScrollTargetKey(targetKey)
    })()
    return () => { cancelled = true }
  }, [explorerRevealPath, rootKey, roots, loadDir, expandDirs, consumeExplorerReveal])

  // Scroll the revealed row into view once it has rendered.
  useEffect(() => {
    if (scrollTargetKey && scrollRowRef.current) {
      scrollRowRef.current.scrollIntoView({ block: 'center' })
      setScrollTargetKey(null)
    }
  }, [scrollTargetKey, childrenCache, expanded])

  const q = filter.trim().toLowerCase()

  const subtreeMatches = useCallback((absKey: string, query: string): boolean => {
    const kids = childrenCache.get(absKey)
    if (!kids) return false
    for (const kid of kids) {
      if (kid.name.toLowerCase().includes(query)) return true
      if (kid.isDir && subtreeMatches(norm(kid.absolutePath), query)) return true
    }
    return false
  }, [childrenCache])

  const renderChildren = (dirKey: string, depth: number): JSX.Element => {
    const kids = childrenCache.get(dirKey)
    if (kids === undefined) {
      return errored.has(dirKey)
        ? <DirTreePlaceholder depth={depth}>{t('viewer.explorerLoadFailed')}</DirTreePlaceholder>
        : <DirTreeSkeleton depth={depth} ariaLabel={t('quickOpen.loading')} />
    }
    const visible = q
      ? kids.filter((k) => k.name.toLowerCase().includes(q) || (k.isDir && subtreeMatches(norm(k.absolutePath), q)))
      : kids
    if (visible.length === 0) {
      return <DirTreePlaceholder depth={depth}>{q ? t('viewer.explorerNoMatch') : t('viewer.explorerEmpty')}</DirTreePlaceholder>
    }
    return <>{visible.map((entry) => renderNode(entry, depth))}</>
  }

  const renderNode = (entry: DirEntryWire, depth: number): JSX.Element => {
    const key = norm(entry.absolutePath)
    const isOpen = entry.isDir && (expanded.has(key) || (q !== '' && subtreeMatches(key, q)))
    const isScrollTarget = scrollTargetKey === key
    return (
      <Fragment key={key}>
        <ActionTooltip label={entry.relativePath} wrapperStyle={{ display: 'block', minWidth: 0, flexShrink: 1 }}>
        <DirTreeRow
          entry={entry}
          depth={depth}
          isOpen={isOpen}
          rowRef={isScrollTarget ? scrollRowRef : undefined}
          onActivate={() => { entry.isDir ? toggleDir(entry.absolutePath) : void openFileEntry(entry) }}
          onContextMenu={(event) => {
            event.preventDefault()
            event.stopPropagation()
            setContextMenu({
              position: { x: event.clientX, y: event.clientY },
              targetPath: entry.absolutePath,
              isDirectory: entry.isDir
            })
          }}
        />
        </ActionTooltip>
        {isOpen && renderChildren(key, depth + 1)}
      </Fragment>
    )
  }

  return (
    <div style={panelStyle}>
      {roots.length > 1 && root && (
        <ExplorerRootPicker roots={roots} selectedRoot={root} onSelect={setSelectedRoot} />
      )}
      <div style={toolbarStyle}>
        <div style={searchWrapStyle}>
          <Search size={13} aria-hidden style={{ color: 'var(--text-secondary)', flexShrink: 0 }} />
          <Input
            bare
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
          ? <DirTreePlaceholder depth={0}>{t('viewer.explorerNoWorkspace')}</DirTreePlaceholder>
          : renderChildren(rootKey, 0)}
      </div>

      {contextMenu && (
        <ReferencePathContextMenu
          position={contextMenu.position}
          targetPath={contextMenu.targetPath}
          allowAddToChat={!contextMenu.isDirectory}
          onClose={() => setContextMenu(null)}
        />
      )}
    </div>
  )
}

const panelStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  height: '100%',
  minWidth: 0,
  background: 'transparent'
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
