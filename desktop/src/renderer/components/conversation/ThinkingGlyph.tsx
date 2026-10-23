import { useId } from 'react'
import styles from './ThinkingGlyph.module.css'

const SIZE = 16

export function ThinkingGlyph({ thinking }: { thinking: boolean }): JSX.Element {
  const filter = `thinking-glyph-${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`
  const edge = Math.min(20, Math.max(4, (SIZE * (window.devicePixelRatio || 1)) / 4))

  return (
    <svg
      className={styles.glyph}
      data-thinking={thinking || undefined}
      width={SIZE}
      height={SIZE}
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden="true"
    >
      <defs>
        <filter id={filter} x="-50%" y="-50%" width="200%" height="200%">
          <feGaussianBlur in="SourceGraphic" stdDeviation="1.15" result="blur" />
          <feColorMatrix in="blur" values={`1 0 0 0 0  0 1 0 0 0  0 0 1 0 0  0 0 0 ${edge} ${-0.4 * edge}`} />
        </filter>
      </defs>
      <g filter={`url(#${filter})`}>
        <g className={styles.spin}>
          {[0, 120, 240].map((angle) => (
            <g key={angle} transform={`rotate(${angle} 12 12)`}>
              <circle className={styles.drop} cx={12} cy={12} r={3.2} />
            </g>
          ))}
        </g>
        <circle className={styles.core} cx={12} cy={12} r={2.6} />
      </g>
    </svg>
  )
}
