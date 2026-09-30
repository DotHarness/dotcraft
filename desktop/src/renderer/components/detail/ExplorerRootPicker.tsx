import { useRef, useState } from 'react'
import { ChevronDown, Folder } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { rootLabel } from '../../utils/viewerRoots'
import styles from './ExplorerRootPicker.module.css'

interface ExplorerRootPickerProps {
  roots: readonly string[]
  selectedRoot: string
  onSelect: (root: string) => void
}

export function ExplorerRootPicker({ roots, selectedRoot, onSelect }: ExplorerRootPickerProps): JSX.Element {
  const t = useT()
  const triggerRef = useRef<HTMLButtonElement>(null)
  const [position, setPosition] = useState<ContextMenuPosition | null>(null)

  function toggle(): void {
    if (position) {
      setPosition(null)
      return
    }
    const rect = triggerRef.current?.getBoundingClientRect()
    if (rect) setPosition({ x: rect.left, y: rect.bottom + 4 })
  }

  return (
    <div className={styles.row}>
      <button
        ref={triggerRef}
        type="button"
        className={styles.trigger}
        aria-label={t('viewer.explorerChooseRoot')}
        aria-haspopup="menu"
        aria-expanded={position !== null}
        title={selectedRoot}
        onMouseDown={(event) => { if (position) event.stopPropagation() }}
        onClick={toggle}
      >
        <Folder size={14} aria-hidden className={styles.icon} />
        <span className={styles.label}>{rootLabel(selectedRoot)}</span>
        <ChevronDown size={13} aria-hidden className={styles.chevron} />
      </button>
      {position && (
        <ContextMenu
          position={position}
          onClose={() => setPosition(null)}
          items={roots.map((root) => ({
            label: rootLabel(root),
            title: root,
            icon: <Folder size={14} aria-hidden />,
            selection: 'radio' as const,
            checked: root === selectedRoot,
            onClick: () => onSelect(root)
          }))}
        />
      )}
    </div>
  )
}
