import type { CSSProperties, JSX } from 'react'
import { SquareTerminal } from 'lucide-react'
import codexIcon from '../../../../assets/agents/codex.svg'
import cursorIcon from '../../../../assets/agents/cursor.svg'
import { SparkIcon } from '../../../ui/AppIcons'

interface AgentIconProps {
  /** Profile name (e.g. `native`, `codex-cli`, `cursor-cli`, or a custom name). */
  name: string
  /** Whether the profile is a DotCraft built-in. */
  isBuiltIn?: boolean
  /** Pixel size of the icon (both width and height). */
  size?: number
  /** Render a rounded tile backdrop behind the icon. */
  framed?: boolean
}

/**
 * Maps a sub-agent profile identity to its visual.
 *
 * - `codex-cli` uses the Codex brand SVG.
 * - `cursor-cli` uses the Cursor brand SVG.
 * - `native` uses the DotCraft spark glyph.
 * - Anything else (custom profiles) falls back to a terminal glyph.
 */
export function AgentIcon({
  name,
  isBuiltIn = false,
  size = 28,
  framed = true
}: AgentIconProps): JSX.Element {
  const art = renderArt(name, isBuiltIn, size)
  if (!framed) return <span style={inlineWrapperStyle(size)}>{art}</span>
  return <span style={frameStyle(size)}>{art}</span>
}

function renderArt(name: string, isBuiltIn: boolean, size: number): JSX.Element {
  if (name === 'codex-cli') {
    return <img src={codexIcon} alt="" width={size} height={size} style={IMG_STYLE} />
  }
  if (name === 'cursor-cli') {
    return (
      <span style={{ ...CURSOR_TINT, width: size, height: size, display: 'inline-flex' }}>
        <img src={cursorIcon} alt="" width={size} height={size} style={IMG_STYLE} />
      </span>
    )
  }
  if (name === 'native') {
    return <SparkIcon size={Math.round(size * 0.72)} />
  }
  if (isBuiltIn) {
    return <SparkIcon size={Math.round(size * 0.72)} />
  }
  return <SquareTerminal size={Math.round(size * 0.7)} strokeWidth={1.8} aria-hidden="true" />
}

const IMG_STYLE: CSSProperties = {
  width: '100%',
  height: '100%',
  objectFit: 'contain',
  display: 'block'
}

const CURSOR_TINT: CSSProperties = {
  color: 'var(--text-primary)'
}

function frameStyle(size: number): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: size + 12,
    height: size + 12,
    borderRadius: '10px',
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
