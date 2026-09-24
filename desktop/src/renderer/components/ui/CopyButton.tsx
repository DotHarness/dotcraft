import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { Check, Copy } from 'lucide-react'
import { IconButton } from './IconButton'

const COPIED_RESET_MS = 1500

export interface CopyButtonProps {
  getText: () => string
  label: string
  copiedLabel: string
  size?: number
  /** `null` follows the surface's control band instead of the compact 6px. */
  radius?: number | null
  iconSize?: number
  tooltipPlacement?: 'top' | 'bottom' | 'left' | 'right'
  tooltipWrapperStyle?: CSSProperties
  className?: string
  style?: CSSProperties
}

export function CopyButton({
  getText,
  label,
  copiedLabel,
  size = 24,
  radius = 6,
  iconSize = 14,
  tooltipPlacement = 'top',
  tooltipWrapperStyle,
  className,
  style
}: CopyButtonProps): JSX.Element {
  const [copied, setCopied] = useState(false)
  const resetTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => () => {
    if (resetTimerRef.current != null) clearTimeout(resetTimerRef.current)
  }, [])

  async function handleCopy(): Promise<void> {
    const text = getText()
    if (!text) return
    try {
      await navigator.clipboard.writeText(text)
    } catch {
      return
    }
    setCopied(true)
    if (resetTimerRef.current != null) clearTimeout(resetTimerRef.current)
    resetTimerRef.current = setTimeout(() => {
      setCopied(false)
      resetTimerRef.current = null
    }, COPIED_RESET_MS)
  }

  const currentLabel = copied ? copiedLabel : label
  return (
    <IconButton
      size={size}
      radius={radius ?? undefined}
      label={currentLabel}
      tooltipLabel={currentLabel}
      tooltipPlacement={tooltipPlacement}
      tooltipWrapperStyle={tooltipWrapperStyle}
      className={className}
      data-copied={copied ? 'true' : undefined}
      onClick={(event) => {
        event.stopPropagation()
        void handleCopy()
      }}
      style={copied ? { ...style, color: 'var(--success)' } : style}
      icon={copied ? <Check size={iconSize} aria-hidden /> : <Copy size={iconSize} aria-hidden />}
    />
  )
}
