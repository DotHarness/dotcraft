/**
 * Changed-files list, docked as a resizable sub-panel on the right of the
 * Changes diff stream — the diff-tab counterpart to `WorkspaceExplorer`.
 *
 * Unlike the workspace explorer it lists *only* the files in the current diff
 * (not the whole tree). Clicking a row expands and scrolls that file's diff
 * section into view; right-click opens the shared chat file-pill menu.
 */
import { useMemo, useState, type CSSProperties } from 'react'
import { Search } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { FileTypeIcon } from '../ui/FileTypeIcon'
import { ReferencePathContextMenu } from '../conversation/ReferencePathContextMenu'
import type { ContextMenuPosition } from '../ui/ContextMenu'
import type { FileDiff } from '../../types/toolCall'

interface ChangesFileListProps {
  files: FileDiff[]
  workspacePath: string
  selectedPath: string | null
  onSelect: (filePath: string) => void
}

interface FileRow {
  diff: FileDiff
  relativePath: string
  name: string
}

export function ChangesFileList({
  files,
  workspacePath,
  selectedPath,
  onSelect
}: ChangesFileListProps): JSX.Element {
  const t = useT()
  const [filter, setFilter] = useState('')
  const [contextMenu, setContextMenu] = useState<{ position: ContextMenuPosition; targetPath: string } | null>(null)

  const rows = useMemo<FileRow[]>(() => {
    return files.map((diff) => {
      const relativePath = toRelativePath(diff.filePath, workspacePath)
      const name = relativePath.split('/').pop() ?? relativePath
      return { diff, relativePath, name }
    })
  }, [files, workspacePath])

  const q = filter.trim().toLowerCase()
  const visible = q ? rows.filter((row) => row.relativePath.toLowerCase().includes(q)) : rows

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

      <div role="list" aria-label={t('changes.fileListTitle')} style={listStyle}>
        {visible.length === 0
          ? <div style={placeholderStyle}>{t('viewer.explorerNoMatch')}</div>
          : visible.map((row) => {
              const isReverted = row.diff.status === 'reverted'
              const selected = selectedPath === row.diff.filePath
              return (
                <div
                  key={row.diff.filePath}
                  role="listitem"
                  title={row.relativePath}
                  onClick={() => onSelect(row.diff.filePath)}
                  onContextMenu={(event) => {
                    event.preventDefault()
                    event.stopPropagation()
                    setContextMenu({ position: { x: event.clientX, y: event.clientY }, targetPath: resolveAbsolutePath(row.diff.filePath, workspacePath) })
                  }}
                  style={{ ...rowStyle, background: selected ? 'var(--bg-tertiary)' : 'transparent' }}
                  onMouseEnter={(e) => { if (!selected) (e.currentTarget as HTMLDivElement).style.background = 'var(--bg-tertiary)' }}
                  onMouseLeave={(e) => { if (!selected) (e.currentTarget as HTMLDivElement).style.background = 'transparent' }}
                >
                  <FileTypeIcon path={row.name} size={15} />
                  <span style={{ ...rowLabelStyle, color: isReverted ? 'var(--text-dimmed)' : 'var(--text-primary)' }}>
                    {row.name}
                  </span>
                  <RowStats additions={row.diff.additions} deletions={row.diff.deletions} dim={isReverted} />
                </div>
              )
            })}
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

function RowStats({ additions, deletions, dim }: { additions: number; deletions: number; dim: boolean }): JSX.Element {
  return (
    <span style={statsStyle}>
      {additions > 0 && <span style={{ color: dim ? 'var(--text-dimmed)' : 'var(--success)' }}>+{additions}</span>}
      {deletions > 0 && <span style={{ color: dim ? 'var(--text-dimmed)' : 'var(--error)' }}>-{deletions}</span>}
    </span>
  )
}

function toRelativePath(filePath: string, workspacePath: string): string {
  if (!workspacePath) return filePath
  const ws = workspacePath.replace(/\\/g, '/').replace(/\/$/, '')
  const fp = filePath.replace(/\\/g, '/')
  if (fp.startsWith(ws + '/')) return fp.slice(ws.length + 1)
  return filePath
}

function resolveAbsolutePath(filePath: string, workspacePath: string): string {
  if (isAbsolutePath(filePath) || !workspacePath) return filePath
  const separator = workspacePath.includes('\\') ? '\\' : '/'
  return `${workspacePath.replace(/[\\/]$/, '')}${separator}${filePath.replace(/^[\\/]/, '')}`
}

function isAbsolutePath(filePath: string): boolean {
  return /^[A-Za-z]:[\\/]/.test(filePath) || filePath.startsWith('/') || filePath.startsWith('\\\\')
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

const listStyle: CSSProperties = {
  flex: 1,
  overflowY: 'auto',
  overflowX: 'hidden',
  padding: '4px 0'
}

const rowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '6px',
  height: '26px',
  padding: '0 8px',
  cursor: 'pointer',
  userSelect: 'none',
  transition: 'background-color 80ms ease'
}

const rowLabelStyle: CSSProperties = {
  flex: 1,
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  fontFamily: 'var(--font-mono)',
  fontSize: '12px'
}

const statsStyle: CSSProperties = {
  display: 'inline-flex',
  gap: '6px',
  flexShrink: 0,
  fontFamily: 'var(--font-mono)',
  fontSize: '11px'
}

const placeholderStyle: CSSProperties = {
  padding: '6px 10px',
  fontSize: '12px',
  color: 'var(--text-secondary)',
  whiteSpace: 'nowrap',
  overflow: 'hidden',
  textOverflow: 'ellipsis'
}
