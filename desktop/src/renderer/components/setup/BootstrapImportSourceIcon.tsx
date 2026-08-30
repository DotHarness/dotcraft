import type { CSSProperties, JSX } from 'react'
import { SquareTerminal } from 'lucide-react'
import type { WorkspaceSetupBootstrapImportSourceId } from '../../../preload/api'
import claudeIcon from '../../assets/agents/claude.svg'

interface BootstrapImportSourceIconProps {
  source: WorkspaceSetupBootstrapImportSourceId
  size?: number
  framed?: boolean
}

const SOURCE_ICON_MAP: Record<WorkspaceSetupBootstrapImportSourceId, string> = {
  claude: claudeIcon
}

export function getBootstrapImportSourceIconSrc(source: WorkspaceSetupBootstrapImportSourceId): string | null {
  return SOURCE_ICON_MAP[source] ?? null
}

export function BootstrapImportSourceIcon({
  source,
  size = 28,
  framed = true
}: BootstrapImportSourceIconProps): JSX.Element {
  const art = renderArt(source, size)
  if (!framed) return <span style={inlineWrapperStyle(size)}>{art}</span>
  return <span style={frameStyle(size)}>{art}</span>
}

function renderArt(source: WorkspaceSetupBootstrapImportSourceId, size: number): JSX.Element {
  const iconSrc = getBootstrapImportSourceIconSrc(source)
  if (iconSrc) {
    return <img src={iconSrc} alt="" width={size} height={size} style={IMG_STYLE} />
  }
  return <SquareTerminal size={Math.round(size * 0.7)} strokeWidth={1.8} aria-hidden="true" />
}

const IMG_STYLE: CSSProperties = {
  width: '75%',
  height: '75%',
  objectFit: 'contain',
  display: 'block'
}

function frameStyle(size: number): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: size + 12,
    height: size + 12,
    borderRadius: 'var(--identity-mark-radius-list)',
    background: 'var(--bg-tertiary)',
    border: '1px solid var(--border-default)',
    color: 'var(--text-primary)',
    flexShrink: 0
  }
}

function inlineWrapperStyle(size: number): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: size,
    height: size,
    color: 'var(--text-primary)',
    flexShrink: 0
  }
}
