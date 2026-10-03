import { useCallback, useRef, useState, type CSSProperties, type ReactNode, type Ref } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { useUIStore } from '../../stores/uiStore'
import { addToast } from '../../stores/toastStore'
import { DisclosureChevron } from '../ui/DisclosureChevron'
import { FileTypeIcon } from '../ui/FileTypeIcon'
import { Skeleton } from '../ui/Skeleton'
import type { DirEntryWire } from '../../../shared/viewer/types'

export const dirKey = (p: string): string => p.replace(/\\/g, '/').replace(/\/+$/, '')

export interface DirTree {
  childrenCache: Map<string, DirEntryWire[]>
  expanded: Set<string>
  errored: Set<string>
  loadDir: (absDir: string) => Promise<void>
  toggleDir: (absDir: string) => void
  expandDirs: (absDirs: string[]) => void
  reset: () => void
}

export function useDirTree(): DirTree {
  const [childrenCache, setChildrenCache] = useState<Map<string, DirEntryWire[]>>(new Map())
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [errored, setErrored] = useState<Set<string>>(new Set())
  const loadingRef = useRef<Set<string>>(new Set())

  const loadDir = useCallback(async (absDir: string): Promise<void> => {
    const key = dirKey(absDir)
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

  const toggleDir = useCallback((absDir: string): void => {
    const key = dirKey(absDir)
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

  const expandDirs = useCallback((absDirs: string[]): void => {
    setExpanded((prev) => new Set([...prev, ...absDirs.map(dirKey)]))
  }, [])

  const reset = useCallback((): void => {
    setChildrenCache(new Map())
    setExpanded(new Set())
    setErrored(new Set())
    loadingRef.current = new Set()
  }, [])

  return { childrenCache, expanded, errored, loadDir, toggleDir, expandDirs, reset }
}

export function useOpenDirEntry(): (entry: DirEntryWire) => Promise<void> {
  const t = useT()
  const currentThreadId = useViewerTabStore((s) => s.currentThreadId)
  const openFile = useViewerTabStore((s) => s.openFile)
  const setActiveViewerTab = useUIStore((s) => s.setActiveViewerTab)

  return useCallback(async (entry: DirEntryWire): Promise<void> => {
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
}

interface DirTreeRowProps {
  entry: DirEntryWire
  depth: number
  isOpen: boolean
  selected?: boolean
  rowRef?: Ref<HTMLDivElement>
  tabIndex?: number
  onActivate: () => void
  onContextMenu?: (event: React.MouseEvent<HTMLDivElement>) => void
  onKeyDown?: (event: React.KeyboardEvent<HTMLDivElement>) => void
}

export function DirTreeRow({
  entry,
  depth,
  isOpen,
  selected = false,
  rowRef,
  tabIndex,
  onActivate,
  onContextMenu,
  onKeyDown
}: DirTreeRowProps): JSX.Element {
  return (
    <div
      ref={rowRef}
      role="treeitem"
      className="dc-dir-tree-row"
      data-path={dirKey(entry.absolutePath)}
      aria-expanded={entry.isDir ? isOpen : undefined}
      aria-selected={selected || undefined}
      tabIndex={tabIndex}
      onClick={onActivate}
      onContextMenu={onContextMenu}
      onKeyDown={onKeyDown}
      style={{ ...dirTreeRowStyle, paddingLeft: 8 + depth * 14 }}
    >
      <span style={chevronSlotStyle}>
        {entry.isDir && <DisclosureChevron expanded={isOpen} />}
      </span>
      <FileTypeIcon path={entry.name} size={15} dir={entry.isDir} expanded={isOpen} />
      <span style={rowLabelStyle}>{entry.name}</span>
    </div>
  )
}

export function DirTreeSkeleton({ depth, ariaLabel }: { depth: number; ariaLabel: string }): JSX.Element {
  return (
    <div role="status" aria-busy="true" aria-label={ariaLabel}>
      {[64, 48, 56].map((width, index) => (
        <div
          key={index}
          aria-hidden="true"
          style={{ ...dirTreeRowStyle, cursor: 'default', paddingLeft: 8 + depth * 14 }}
        >
          <span style={chevronSlotStyle} />
          <Skeleton width={15} height={15} radius={4} />
          <Skeleton width={`${width}%`} height={11} />
        </div>
      ))}
    </div>
  )
}

export function DirTreePlaceholder({ depth, children }: { depth: number; children: ReactNode }): JSX.Element {
  return (
    <div style={{ ...placeholderStyle, paddingLeft: 8 + depth * 14 + 17 }}>
      {children}
    </div>
  )
}

const dirTreeRowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '5px',
  height: '24px',
  paddingRight: '8px',
  cursor: 'pointer',
  fontSize: '13px',
  color: 'var(--text-primary)',
  userSelect: 'none'
}

const chevronSlotStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  width: '14px',
  flexShrink: 0,
  color: 'var(--text-secondary)'
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
