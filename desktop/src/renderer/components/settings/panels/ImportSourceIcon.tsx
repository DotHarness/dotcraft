import type { CSSProperties, JSX } from 'react'
import { Download } from 'lucide-react'
import claudeIcon from '../../../assets/agents/claude.svg'
import cursorIcon from '../../../assets/agents/cursor.svg'
import openAiIcon from '../../../assets/agents/openai.svg'

interface ImportSourceIconProps {
  source: string
  /** Footprint of the icon in pixels; the glyph is scaled inside it per source so the marks read the same size. */
  size?: number
  /** Render a rounded tile backdrop behind the icon. */
  framed?: boolean
}

/** Each mark fills its 24px viewBox differently, so the scale evens out their optical size. */
const SOURCE_ART: Record<string, { src: string; scale: number }> = {
  'claude-code': { src: claudeIcon, scale: 0.6 },
  codex: { src: openAiIcon, scale: 0.6 },
  cursor: { src: cursorIcon, scale: 0.62 }
}

export function ImportSourceIcon({ source, size = 36, framed = true }: ImportSourceIconProps): JSX.Element {
  const art = SOURCE_ART[source]
  const glyph = Math.round(size * (art?.scale ?? 0.5))
  return (
    <span style={framed ? frameStyle(size) : inlineStyle(size)}>
      {art
        ? <img src={art.src} alt="" width={glyph} height={glyph} style={{ width: glyph, height: glyph, display: 'block' }} />
        : <Download size={glyph} strokeWidth={1.8} aria-hidden="true" />}
    </span>
  )
}

function frameStyle(size: number): CSSProperties {
  return {
    ...inlineStyle(size),
    borderRadius: 'var(--identity-mark-radius-list)',
    background: 'var(--bg-tertiary)',
    border: '1px solid var(--border-default)'
  }
}

function inlineStyle(size: number): CSSProperties {
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
