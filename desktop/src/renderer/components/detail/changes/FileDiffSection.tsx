import { useState, type CSSProperties, type KeyboardEvent } from 'react'
import { ChevronDown, ChevronUp, FolderOpen } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import type { ChangesDiffMode } from '../../../stores/uiStore'
import type { TurnFileChange } from '../../../types/turnDiff'
import { ActionTooltip } from '../../ui/ActionTooltip'
import { CopyButton } from '../../ui/CopyButton'
import { IconButton } from '../../ui/IconButton'
import { DiffViewer } from '../DiffViewer'
import { ChangePath } from './ChangePath'
import styles from './FileDiffSection.module.css'

interface FileDiffSectionProps {
  row: TurnFileChange
  workspacePath: string
  mode: ChangesDiffMode
  wordWrap: boolean
  expanded: boolean
  registerSection: (key: string, node: HTMLElement | null) => void
  onToggle: () => void
}

export function FileDiffSection({
  row,
  workspacePath,
  mode,
  wordWrap,
  expanded,
  registerSection,
  onToggle
}: FileDiffSectionProps): JSX.Element {
  const t = useT()
  const [active, setActive] = useState(false)
  const file = row.diff
  const isReverted = file.status === 'reverted'
  const relativePath = toRelativePath(file.filePath, workspacePath)
  const hoverActionStyle: CSSProperties = { opacity: active ? 1 : 0, pointerEvents: active ? 'auto' : 'none' }

  function handleHeaderKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
    if (event.key !== 'Enter' && event.key !== ' ') return
    event.preventDefault()
    onToggle()
  }

  async function openParentFolder(): Promise<void> {
    const target = resolveAbsolutePath(file.filePath, workspacePath)
    if (!target) return
    try {
      await window.api.shell.showItemInFolder(target)
    } catch (err) {
      console.error('Open folder failed:', err)
    }
  }

  return (
    <section
      ref={(node) => registerSection(row.key, node)}
      data-change-key={row.key}
      onMouseEnter={() => setActive(true)}
      onMouseLeave={() => setActive(false)}
      onFocusCapture={() => setActive(true)}
      onBlurCapture={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
          setActive(false)
        }
      }}
    >
      <ActionTooltip label={relativePath} wrapperStyle={{ display: 'block' }}>
      <div
        role="button"
        tabIndex={0}
        aria-expanded={expanded}
        onClick={onToggle}
        onKeyDown={handleHeaderKeyDown}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          minHeight: '34px',
          padding: '5px 10px',
          color: isReverted ? 'var(--text-dimmed)' : 'var(--text-primary)',
          background: expanded ? 'var(--bg-primary)' : 'transparent',
          cursor: 'pointer',
          userSelect: 'none'
        }}
      >
        <span className={styles.label}>
          <ChangePath path={relativePath} />
          {isReverted && <span className={styles.reverted}>{t('changesFile.reverted')}</span>}
        </span>
        <CopyButton
          getText={() => relativePath}
          label={t('changesFile.copyPath')}
          copiedLabel={t('common.copied')}
          tooltipPlacement="bottom"
          style={hoverActionStyle}
        />
        <IconButton
          icon={<FolderOpen size={14} strokeWidth={1.8} aria-hidden />}
          label={t('changesFile.openFolder')}
          tooltipLabel={t('changesFile.openFolder')}
          tooltipPlacement="bottom"
          size={24}
          radius={5}
          onClick={(event) => {
            event.stopPropagation()
            void openParentFolder()
          }}
          style={hoverActionStyle}
        />
        <FileStats additions={file.additions} deletions={file.deletions} dim={isReverted} />
        <span style={{ color: 'var(--text-secondary)', width: '16px', display: 'inline-flex', justifyContent: 'center' }}>
          {expanded ? <ChevronUp size={15} strokeWidth={1.8} /> : <ChevronDown size={15} strokeWidth={1.8} />}
        </span>
      </div>
      </ActionTooltip>
      {expanded && (
        <div style={{ background: 'var(--bg-primary)' }}>
          {row.truncated
            ? <p className={styles.truncatedNote}>{t('changes.truncated')}</p>
            : <DiffViewer diff={file} workspacePath={workspacePath} mode={mode} wordWrap={wordWrap} />}
        </div>
      )}
    </section>
  )
}

export function FileStats({
  additions,
  deletions,
  dim = false
}: {
  additions: number
  deletions: number
  dim?: boolean
}): JSX.Element {
  return (
    <span style={{ display: 'inline-flex', gap: '6px', flexShrink: 0, fontFamily: 'var(--font-mono)', fontSize: '12px' }}>
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
  const rel = filePath.replace(/^[\\/]/, '').replace(/[\\/]/g, separator)
  return `${workspacePath.replace(/[\\/]$/, '')}${separator}${rel}`
}

function isAbsolutePath(filePath: string): boolean {
  return /^[A-Za-z]:[\\/]/.test(filePath) || filePath.startsWith('/') || filePath.startsWith('\\\\')
}
