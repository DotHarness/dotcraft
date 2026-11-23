import { useState, type JSX, type ReactNode } from 'react'

type Handoff = 'sweep' | 'fade'

interface ShownLabel {
  handoffKey: string
  label: string
  outgoing: string | null
  handoff: Handoff | null
  seq: number
  active: boolean
}

export function ToolLabelHandoff({
  handoffKey,
  label,
  active,
  children
}: {
  handoffKey: string
  label: string
  active: boolean
  children: ReactNode
}): JSX.Element {
  const [shown, setShown] = useState<ShownLabel>({ handoffKey, label, outgoing: null, handoff: null, seq: 0, active })
  if (handoffKey !== shown.handoffKey) {
    const handoff: Handoff | null = active ? 'sweep' : shown.active ? 'fade' : null
    setShown({ handoffKey, label, outgoing: handoff ? shown.label : null, handoff, seq: shown.seq + 1, active })
  } else if (label !== shown.label || active !== shown.active) {
    setShown({ ...shown, label, active })
  }

  return (
    <span className="dc-tool-handoff">
      {shown.outgoing ? (
        <span
          key={`out-${shown.seq}`}
          className="dc-tool-handoff-outgoing"
          aria-hidden
          onAnimationEnd={() => setShown((current) => ({ ...current, outgoing: null }))}
        >
          {shown.outgoing}
        </span>
      ) : null}
      <span key={`in-${shown.seq}`} className="dc-tool-handoff-viewport" data-handoff={shown.handoff ?? undefined}>
        <span className="dc-tool-handoff-label">{children}</span>
      </span>
    </span>
  )
}
