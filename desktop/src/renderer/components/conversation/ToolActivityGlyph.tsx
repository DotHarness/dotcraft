import { useId, type JSX } from 'react'
import {
  TERMINAL_GLYPH_CURSOR,
  TERMINAL_GLYPH_FRAME,
  TERMINAL_GLYPH_PROMPT
} from '../ui/TerminalGlyph'
import styles from './ToolActivityGlyph.module.css'

export type ToolActivityMotion = 'command' | 'search' | 'read' | 'web'

interface ToolActivityGlyphProps {
  motion: ToolActivityMotion
  size?: number
  strokeWidth?: number
  className?: string
}

const LEAF_REST = 'M12 7C12 4.79 13.79 3 16 3L21 3C21.55 3 22 3.45 22 4L22 17C22 17.55 21.55 18 21 18L15 18C13.34 18 12 19.34 12 21Z'
const MERIDIAN_REST = ['M12 2C14.21 2 16 6.48 16 12C16 17.52 14.21 22 12 22', 'M12 2C9.79 2 8 6.48 8 12C8 17.52 9.79 22 12 22']
const OUTPUT_LINES = ['M7.5 8.5h9', 'M7.5 12h6', 'M7.5 15.5h7.5']

export function ToolActivityGlyph({
  motion,
  size = 16,
  strokeWidth = 1.6,
  className
}: ToolActivityGlyphProps): JSX.Element {
  const clip = `tool-glyph-${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`
  return (
    <svg
      className={[styles.glyph, className].filter(Boolean).join(' ')}
      data-motion={motion}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={strokeWidth}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {motion === 'command' ? (
        <>
          <defs>
            <clipPath id={clip}>
              <rect x="4" y="4" width="16" height="16" rx="3.5" />
            </clipPath>
          </defs>
          <rect
            x={TERMINAL_GLYPH_FRAME.x}
            y={TERMINAL_GLYPH_FRAME.y}
            width={TERMINAL_GLYPH_FRAME.size}
            height={TERMINAL_GLYPH_FRAME.size}
            rx={TERMINAL_GLYPH_FRAME.radius}
          />
          <g clipPath={`url(#${clip})`}>
            <g className={styles.prompt}>
              <path d={TERMINAL_GLYPH_PROMPT} />
              <path className={styles.cursor} d={TERMINAL_GLYPH_CURSOR} />
            </g>
            <g className={styles.output}>
              {OUTPUT_LINES.map((d) => <path key={d} d={d} />)}
            </g>
          </g>
        </>
      ) : motion === 'search' ? (
        <g className={styles.lens}>
          <path d="m21 21-4.34-4.34" />
          <circle cx="11" cy="11" r="8" />
        </g>
      ) : motion === 'read' ? (
        <>
          <path d="M12 7v14" />
          <path d="M3 18a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h5a4 4 0 0 1 4 4 4 4 0 0 1 4-4h5a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1h-6a3 3 0 0 0-3 3 3 3 0 0 0-3-3z" />
          <path className={styles.leaf} d={LEAF_REST} />
        </>
      ) : (
        <>
          <circle cx="12" cy="12" r="10" />
          <path d="M2 12h20" />
          <path className={styles.meridian} d={MERIDIAN_REST[0]} />
          <path className={styles.meridian} data-trailing="" d={MERIDIAN_REST[1]} />
        </>
      )}
    </svg>
  )
}
