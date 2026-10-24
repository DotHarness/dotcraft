import { useEffect, useId, useRef, useState, type CSSProperties } from 'react'
import styles from './ThinkingGlyph.module.css'

const SIZE = 16
const LOOP_MS = 3000
const FUSED_MS = 1350
const FUSED_CORE = 1.25
const SETTLE_MS = 640

export function ThinkingGlyph({ thinking, onSettled }: { thinking: boolean; onSettled?: () => void }): JSX.Element {
  const filter = `thinking-glyph-${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`
  const edge = Math.min(20, Math.max(4, (SIZE * (window.devicePixelRatio || 1)) / 4))
  const ref = useRef<SVGSVGElement>(null)
  const settledRef = useRef(onSettled)
  settledRef.current = onSettled
  const settles = onSettled !== undefined
  const [settleFrom, setSettleFrom] = useState<number | null>(null)

  useEffect(() => {
    setSettleFrom(null)
    if (thinking || !settles) return
    let timer: number | undefined
    const settle = (from: number): void => {
      setSettleFrom(from)
      timer = window.setTimeout(() => settledRef.current?.(), SETTLE_MS)
    }
    const drop = ref.current?.querySelector(`.${styles.drop}`)?.getAnimations?.()[0]
    const core = ref.current?.querySelector(`.${styles.core}`)
    if (!drop || !core) {
      settle(FUSED_CORE)
      return () => window.clearTimeout(timer)
    }
    const time = Number(drop.currentTime ?? 0)
    const fusedAt = time + ((FUSED_MS - (time % LOOP_MS) + LOOP_MS) % LOOP_MS)
    let frame = requestAnimationFrame(function waitForFuse() {
      if (Number(drop.currentTime ?? fusedAt) < fusedAt) {
        frame = requestAnimationFrame(waitForFuse)
        return
      }
      settle(new DOMMatrix(getComputedStyle(core).transform).a)
    })
    return () => {
      cancelAnimationFrame(frame)
      window.clearTimeout(timer)
    }
  }, [thinking, settles])

  const looping = thinking || (settles && settleFrom === null)

  return (
    <svg
      ref={ref}
      className={styles.glyph}
      data-thinking={looping || undefined}
      data-settling={settleFrom !== null || undefined}
      style={settleFrom !== null ? { '--core-from': settleFrom } as CSSProperties : undefined}
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
