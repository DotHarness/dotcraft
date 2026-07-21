/**
 * The Changes header "…" overflow menu.
 *
 * Mirrors `ViewerActionsMenu` styling. Holds the view preferences that don't
 * warrant a dedicated toolbar button: word-wrap toggle and expand / collapse all
 * file diffs. Rendered through a portal so the menu is never clipped by the
 * panel body's `overflow: hidden`.
 */
import { useRef, useState } from 'react'
import { ChevronsDownUp, ChevronsUpDown, MoreHorizontal, WrapText } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { IconButton } from '../ui/IconButton'

interface ChangesActionsMenuProps {
  wordWrap: boolean
  onToggleWordWrap: () => void
  onExpandAll: () => void
  onCollapseAll: () => void
}

export function ChangesActionsMenu({
  wordWrap,
  onToggleWordWrap,
  onExpandAll,
  onCollapseAll
}: ChangesActionsMenuProps): JSX.Element {
  const t = useT()
  const buttonRef = useRef<HTMLButtonElement>(null)
  const [position, setPosition] = useState<ContextMenuPosition | null>(null)

  function toggleOpen(): void {
    if (position) {
      setPosition(null)
      return
    }
    const rect = buttonRef.current?.getBoundingClientRect()
    if (rect) setPosition({ x: rect.right - 200, y: rect.bottom + 4 })
  }

  function closeMenu(): void {
    setPosition(null)
    window.setTimeout(() => buttonRef.current?.focus(), 0)
  }

  return (
    <>
      <IconButton
        ref={buttonRef}
        size={28}
        label={t('viewer.moreActions')}
        tooltipLabel={t('viewer.moreActions')}
        tooltipPlacement="bottom"
        aria-haspopup="menu"
        aria-expanded={position != null}
        onClick={toggleOpen}
        icon={<MoreHorizontal size={16} aria-hidden style={{ display: 'block' }} />}
      />
      {position && (
        <ContextMenu
          position={position}
          onClose={closeMenu}
          items={[
            { label: wordWrap ? t('viewer.disableWordWrap') : t('viewer.enableWordWrap'), icon: <WrapText size={15} />, onClick: onToggleWordWrap },
            { label: t('changes.expandAll'), icon: <ChevronsUpDown size={15} />, onClick: onExpandAll },
            { label: t('changes.collapseAll'), icon: <ChevronsDownUp size={15} />, onClick: onCollapseAll }
          ]}
        />
      )}
    </>
  )
}
