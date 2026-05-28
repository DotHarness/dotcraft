import { ChevronRight } from 'lucide-react'
import type { JSX } from 'react'

interface ToolCollapseChevronProps {
  expanded: boolean
  visible?: boolean
}

export function ToolCollapseChevron({
  expanded,
  visible = true
}: ToolCollapseChevronProps): JSX.Element {
  return (
    <span
      data-testid="tool-disclosure-icon"
      aria-hidden={!visible}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        width: '14px',
        height: '14px',
        color: 'currentColor',
        opacity: visible ? 1 : 0,
        transform: expanded ? 'rotate(90deg)' : 'rotate(0deg)',
        transition: 'transform 150ms ease, opacity 120ms ease'
      }}
    >
      <ChevronRight size={13} strokeWidth={1.8} aria-hidden />
    </span>
  )
}
