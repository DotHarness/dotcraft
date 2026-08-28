/**
 * Rendered through a portal so the menu is never clipped by the viewer body's
 * `overflow: hidden`.
 */
import { useRef, useState } from 'react'
import { Copy, FileText, MoreHorizontal, WrapText } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { IconButton } from '../ui/IconButton'

interface ViewerActionsMenuProps {
  absolutePath: string
  /** Whether the active viewer is the text editor (controls word-wrap item). */
  isText: boolean
  wordWrap: boolean
  onToggleWordWrap: () => void
}

export function ViewerActionsMenu({
  absolutePath,
  isText,
  wordWrap,
  onToggleWordWrap
}: ViewerActionsMenuProps): JSX.Element {
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

  async function copyPath(): Promise<void> {
    try {
      await navigator.clipboard.writeText(absolutePath)
      addToast(t('toast.copied'), 'success')
    } catch {
      addToast(t('viewer.actionFailed'), 'warning')
    }
  }

  async function copyContents(): Promise<void> {
    try {
      const result = await window.api.workspace.viewer.readText({ absolutePath })
      await navigator.clipboard.writeText(result.text)
      addToast(t('toast.copied'), 'success')
    } catch {
      addToast(t('viewer.copyContentsFailed'), 'warning')
    }
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
            { label: t('viewer.copyPath'), icon: <Copy size={15} />, onClick: () => { void copyPath() } },
            { label: t('viewer.copyContents'), icon: <FileText size={15} />, onClick: () => { void copyContents() } },
            ...(isText ? [{ label: wordWrap ? t('viewer.disableWordWrap') : t('viewer.enableWordWrap'), icon: <WrapText size={15} />, onClick: onToggleWordWrap }] : [])
          ]}
        />
      )}
    </>
  )
}
