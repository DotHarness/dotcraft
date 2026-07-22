/**
 * Header bar shown above file viewers (text / markdown / image / pdf /
 * unsupported):
 *   [type-icon] path / breadcrumb …            [⋯] [Open ▾] [explorer]
 *
 * Browser and terminal tabs keep their own chrome and never render this header.
 * Chrome stays neutral per the desktop visual-design spec; the only color is
 * the small file-type identity icon.
 */
import { useEffect, useRef, type CSSProperties } from 'react'
import { ChevronRight, Folder, FolderOpen } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { FileTypeIcon } from '../ui/FileTypeIcon'
import { OpenTargetButton } from '../conversation/OpenTargetButton'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ViewerActionsMenu } from './ViewerActionsMenu'
import { IconButton } from '../ui/IconButton'

interface ViewerHeaderProps {
  absolutePath: string
  relativePath: string
  /** Whether the active viewer is the text editor (enables word-wrap toggle). */
  isText: boolean
  wordWrap: boolean
  onToggleWordWrap: () => void
}

/** Splits a relativePath into ordered segments + the forward-slashed abs prefix. */
function breadcrumbSegments(absolutePath: string, relativePath: string): {
  segments: string[]
  /** Forward-slashed workspace root (ends with `/`). */
  rootFwd: string
} {
  const absFwd = absolutePath.replace(/\\/g, '/')
  const rel = relativePath.replace(/\\/g, '/')
  const segments = rel.split('/').filter(Boolean)
  const rootFwd = absFwd.endsWith(rel) ? absFwd.slice(0, absFwd.length - rel.length) : ''
  return { segments, rootFwd }
}

export function ViewerHeader({
  absolutePath,
  relativePath,
  isText,
  wordWrap,
  onToggleWordWrap
}: ViewerHeaderProps): JSX.Element {
  const t = useT()
  const explorerVisible = useUIStore((s) => s.explorerVisible)
  const toggleExplorer = useUIStore((s) => s.toggleExplorer)
  const revealInExplorer = useUIStore((s) => s.revealInExplorer)
  const crumbsRef = useRef<HTMLDivElement>(null)

  const { segments, rootFwd } = breadcrumbSegments(absolutePath, relativePath)

  // Keep the filename (end of the trail) visible by default.
  useEffect(() => {
    const el = crumbsRef.current
    if (el) el.scrollLeft = el.scrollWidth
  }, [relativePath])

  return (
    <div style={headerStyle}>
      <div ref={crumbsRef} style={crumbsScrollStyle}>
        <FileTypeIcon path={relativePath} size={15} style={{ marginRight: 2 }} />
        {segments.map((segment, index) => {
          const isLast = index === segments.length - 1
          if (isLast) {
            return (
              <ActionTooltip key={index} label={relativePath}>
              <span style={crumbFileStyle}>
                {segment}
              </span>
              </ActionTooltip>
            )
          }
          const segAbs = rootFwd + segments.slice(0, index + 1).join('/')
          return (
            <span key={index} style={crumbGroupStyle}>
              <ActionTooltip label={segment}>
              <button
                type="button"
                style={crumbFolderStyle}
                onClick={() => { if (segAbs) revealInExplorer(segAbs) }}
                onMouseEnter={(e) => { (e.currentTarget as HTMLButtonElement).style.color = 'var(--text-primary)' }}
                onMouseLeave={(e) => { (e.currentTarget as HTMLButtonElement).style.color = 'var(--text-secondary)' }}
              >
                {segment}
              </button>
              </ActionTooltip>
              <ChevronRight size={13} aria-hidden style={{ color: 'var(--text-tertiary, var(--text-secondary))', flexShrink: 0, opacity: 0.7 }} />
            </span>
          )
        })}
      </div>

      <div style={actionsStyle}>
        <ViewerActionsMenu
          absolutePath={absolutePath}
          isText={isText}
          wordWrap={wordWrap}
          onToggleWordWrap={onToggleWordWrap}
        />

        <OpenTargetButton
          targetPath={absolutePath}
          tooltipLabel={t('detailPanel.openFileTitle', { path: relativePath })}
          menuAriaLabel={t('detailPanel.openFileMenuAria')}
          showPrimaryLabel
        />

        <IconButton
          size={28}
          label={explorerVisible ? t('viewer.closeExplorer') : t('viewer.openExplorer')}
          tooltipLabel={explorerVisible ? t('viewer.closeExplorer') : t('viewer.openExplorer')}
          tooltipPlacement="bottom"
          aria-pressed={explorerVisible}
          active={explorerVisible}
          onClick={toggleExplorer}
          icon={explorerVisible
              ? <FolderOpen size={16} aria-hidden style={{ display: 'block' }} />
              : <Folder size={16} aria-hidden style={{ display: 'block' }} />}
        />
      </div>
    </div>
  )
}

const headerStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  height: '38px',
  flexShrink: 0,
  padding: '0 8px 0 12px',
  boxSizing: 'border-box',
  borderBottom: '1px solid var(--glass-border)'
}

const crumbsScrollStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '2px',
  flex: 1,
  minWidth: 0,
  overflowX: 'auto',
  overflowY: 'hidden',
  scrollbarWidth: 'none',
  whiteSpace: 'nowrap',
  fontSize: '12px'
}

const crumbGroupStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '2px',
  flexShrink: 0
}

const crumbFolderStyle: CSSProperties = {
  border: 'none',
  background: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  padding: '2px 3px',
  borderRadius: '4px',
  fontSize: '12px',
  lineHeight: 1.2,
  whiteSpace: 'nowrap',
  transition: 'color 100ms ease'
}

const crumbFileStyle: CSSProperties = {
  color: 'var(--text-primary)',
  fontSize: '12px',
  fontWeight: 600,
  padding: '2px 3px',
  whiteSpace: 'nowrap',
  flexShrink: 0
}

const actionsStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '4px',
  flexShrink: 0
}
