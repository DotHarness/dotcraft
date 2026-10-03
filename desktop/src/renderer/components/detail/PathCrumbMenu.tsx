import { Fragment, useContext, useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent } from 'react'
import { createPortal } from 'react-dom'
import { useT } from '../../contexts/LocaleContext'
import { LayerContext } from '../../contexts/LayerContext'
import type { DirEntryWire } from '../../../shared/viewer/types'
import { DirTreePlaceholder, DirTreeRow, DirTreeSkeleton, dirKey, useDirTree, useOpenDirEntry } from './DirTree'

interface PathCrumbMenuProps {
  anchor: HTMLElement
  directoryPath: string
  activePath: string
  expandActive: boolean
  onClose: () => void
}

const MENU_WIDTH = 340
const MENU_HEIGHT = 320

export function PathCrumbMenu({ anchor, directoryPath, activePath, expandActive, onClose }: PathCrumbMenuProps): JSX.Element {
  const t = useT()
  const layerDepth = useContext(LayerContext)
  const { childrenCache, expanded, errored, loadDir, toggleDir, expandDirs } = useDirTree()
  const openEntry = useOpenDirEntry()
  const menuRef = useRef<HTMLDivElement>(null)
  const rootKey = dirKey(directoryPath)
  const activeKey = dirKey(activePath)
  const [focusKey, setFocusKey] = useState(activeKey)
  const revealedRef = useRef(false)

  useEffect(() => {
    void loadDir(directoryPath)
    if (expandActive) {
      expandDirs([activePath])
      void loadDir(activePath)
    }
  }, [directoryPath, activePath, expandActive, loadDir, expandDirs])

  useLayoutEffect(() => {
    if (revealedRef.current) return
    const row = menuRef.current?.querySelector<HTMLElement>(`[data-path="${CSS.escape(activeKey)}"]`)
    if (!row) return
    revealedRef.current = true
    row.scrollIntoView({ block: 'center' })
    row.focus({ preventScroll: true })
  })

  useEffect(() => {
    function handlePointerDown(event: MouseEvent): void {
      const target = event.target as Node
      if (menuRef.current?.contains(target) || anchor.contains(target)) return
      onClose()
    }
    function handleResize(): void { onClose() }
    document.addEventListener('mousedown', handlePointerDown)
    window.addEventListener('resize', handleResize)
    window.addEventListener('blur', handleResize)
    return () => {
      document.removeEventListener('mousedown', handlePointerDown)
      window.removeEventListener('resize', handleResize)
      window.removeEventListener('blur', handleResize)
    }
  }, [anchor, onClose])

  const activate = (entry: DirEntryWire): void => {
    if (entry.isDir) {
      toggleDir(entry.absolutePath)
      return
    }
    onClose()
    void openEntry(entry)
  }

  const visibleRows = (): HTMLElement[] =>
    Array.from(menuRef.current?.querySelectorAll<HTMLElement>('[role="treeitem"]') ?? [])

  const focusRow = (row: HTMLElement | undefined): void => {
    if (!row) return
    setFocusKey(row.dataset.path ?? '')
    row.focus()
    row.scrollIntoView({ block: 'nearest' })
  }

  const handleRowKeyDown = (event: KeyboardEvent<HTMLDivElement>, entry: DirEntryWire, depth: number): void => {
    const rows = visibleRows()
    const index = rows.indexOf(event.currentTarget)
    const key = dirKey(entry.absolutePath)
    const isOpen = entry.isDir && expanded.has(key)
    switch (event.key) {
      case 'ArrowDown':
        focusRow(rows[index + 1])
        break
      case 'ArrowUp':
        focusRow(rows[index - 1])
        break
      case 'Home':
        focusRow(rows[0])
        break
      case 'End':
        focusRow(rows[rows.length - 1])
        break
      case 'ArrowRight':
        if (!entry.isDir) return
        if (isOpen) focusRow(rows[index + 1])
        else toggleDir(entry.absolutePath)
        break
      case 'ArrowLeft':
        if (isOpen) {
          toggleDir(entry.absolutePath)
        } else if (depth > 0) {
          const parentKey = key.slice(0, key.lastIndexOf('/'))
          focusRow(rows.find((row) => row.dataset.path === parentKey))
        }
        break
      case 'Enter':
      case ' ':
        activate(entry)
        break
      case 'Tab':
        onClose()
        return
      default:
        return
    }
    event.preventDefault()
  }

  const renderChildren = (key: string, depth: number): JSX.Element => {
    const kids = childrenCache.get(key)
    if (kids === undefined) {
      return errored.has(key)
        ? <DirTreePlaceholder depth={depth}>{t('viewer.explorerLoadFailed')}</DirTreePlaceholder>
        : <DirTreeSkeleton depth={depth} ariaLabel={t('quickOpen.loading')} />
    }
    if (kids.length === 0) {
      return <DirTreePlaceholder depth={depth}>{t('viewer.explorerEmpty')}</DirTreePlaceholder>
    }
    return (
      <>
        {kids.map((entry) => {
          const entryKey = dirKey(entry.absolutePath)
          const isOpen = entry.isDir && expanded.has(entryKey)
          return (
            <Fragment key={entryKey}>
              <DirTreeRow
                entry={entry}
                depth={depth}
                isOpen={isOpen}
                selected={entryKey === activeKey}
                tabIndex={entryKey === focusKey ? 0 : -1}
                onActivate={() => activate(entry)}
                onKeyDown={(event) => handleRowKeyDown(event, entry, depth)}
              />
              {isOpen && renderChildren(entryKey, depth + 1)}
            </Fragment>
          )
        })}
      </>
    )
  }

  const rect = anchor.getBoundingClientRect()
  const width = Math.min(MENU_WIDTH, window.innerWidth - 16)
  const left = Math.max(8, Math.min(rect.left - 6, window.innerWidth - width - 8))
  const top = rect.bottom + 4
  const height = Math.min(MENU_HEIGHT, window.innerHeight - top - 8)

  return createPortal(
    <div
      ref={menuRef}
      role="tree"
      aria-label={anchor.textContent ?? undefined}
      className="dc-path-crumb-menu"
      onKeyDown={(event) => {
        if (event.key !== 'Escape') return
        event.stopPropagation()
        anchor.focus()
        onClose()
      }}
      style={{
        top,
        left,
        width,
        height,
        zIndex: layerDepth > 0 ? 10100 : 9999
      }}
    >
      {renderChildren(rootKey, 0)}
    </div>,
    document.body
  ) as JSX.Element
}
