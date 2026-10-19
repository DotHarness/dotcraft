import { useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type JSX } from 'react'
import { Split } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'

const MARQUEE_PX_PER_SECOND = 26
const LEFT_FADE_PX = 8

export function ThreadRowTitle({
  title,
  worktreeBranch,
  onVisibilityChange
}: {
  title: string
  worktreeBranch?: string | null
  onVisibilityChange?: (onScreen: boolean) => void
}): JSX.Element {
  const t = useT()
  const outerRef = useRef<HTMLSpanElement>(null)
  const textRef = useRef<HTMLSpanElement>(null)
  const [shift, setShift] = useState(0)

  useLayoutEffect(() => {
    const outer = outerRef.current
    const text = textRef.current
    if (!outer || !text) return
    const measure = (): void => setShift(Math.max(0, Math.ceil(text.scrollWidth - (outer.clientWidth - LEFT_FADE_PX))))
    measure()
    if (typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(measure)
    observer.observe(outer)
    return () => observer.disconnect()
  }, [title])

  useEffect(() => {
    const outer = outerRef.current
    if (!outer || !onVisibilityChange) return
    if (typeof IntersectionObserver === 'undefined') {
      onVisibilityChange(true)
      return
    }
    const observer = new IntersectionObserver((entries) => onVisibilityChange(entries.some((entry) => entry.isIntersecting)))
    observer.observe(outer)
    return () => observer.disconnect()
  }, [onVisibilityChange])

  const overflowing = shift >= 1
  const worktreeLabel = worktreeBranch ? t('threadHeader.worktreeBadge', { branch: worktreeBranch }) : null

  return (
    <span className="dc-thread-name">
      <span
        ref={outerRef}
        className="dc-thread-title"
        data-overflowing={overflowing ? 'true' : undefined}
        style={
          {
            '--dc-title-shift': `${-shift}px`,
            '--dc-title-duration': `${Math.max(0.4, shift / MARQUEE_PX_PER_SECOND)}s`
          } as CSSProperties
        }
      >
        <span ref={textRef} className="dc-thread-title__text">
          {title}
        </span>
      </span>
      {worktreeLabel && (
        <ActionTooltip label={worktreeLabel} placement="top">
          <span className="dc-thread-worktree-mark" role="img" aria-label={worktreeLabel}>
            <Split size={14} strokeWidth={2} aria-hidden />
          </span>
        </ActionTooltip>
      )}
    </span>
  )
}
