/**
 * Colorful per-file-type icon (VS Code Icons via Iconify).
 *
 * Renders a neutral lucide glyph until the icon collection has been registered
 * (see `ensureVscodeIcons`), then swaps to the colored type icon. Used by the
 * viewer header/breadcrumb, the workspace explorer, viewer tabs, QuickOpen
 * results, and chat file pills so every file reference shares one icon system.
 */
import { useEffect, type CSSProperties } from 'react'
import { Icon } from '@iconify/react'
import { FileText, Folder, FolderOpen } from 'lucide-react'
import {
  ensureVscodeIcons,
  fileIconName,
  useIconsReady
} from '../../utils/fileTypeIcons'

interface FileTypeIconProps {
  /** File or folder path/name used to resolve the icon. */
  path: string
  /** Pixel size (square). */
  size?: number
  /** Render a folder icon instead of a file icon. */
  dir?: boolean
  /** For folders: show the opened variant. */
  expanded?: boolean
  style?: CSSProperties
}

export function FileTypeIcon({
  path,
  size = 14,
  dir = false,
  expanded = false,
  style
}: FileTypeIconProps): JSX.Element {
  const ready = useIconsReady()

  useEffect(() => {
    void ensureVscodeIcons()
  }, [])

  const baseStyle: CSSProperties = { display: 'block', flexShrink: 0, ...style }

  if (!ready) {
    const Fallback = dir ? (expanded ? FolderOpen : Folder) : FileText
    return (
      <Fallback
        size={size}
        strokeWidth={2}
        aria-hidden
        style={{ color: 'var(--text-secondary)', ...baseStyle }}
      />
    )
  }

  return (
    <Icon
      icon={fileIconName(path, { dir, expanded })}
      width={size}
      height={size}
      aria-hidden
      style={baseStyle}
    />
  )
}
