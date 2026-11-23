import { useState, type JSX, type ReactNode } from 'react'

interface ShownIcon {
  iconKey: string
  node: ReactNode
  outgoing: ReactNode | null
  seq: number
}

export function ToolIconHandoff({
  iconKey,
  children
}: {
  iconKey: string
  children: ReactNode
}): JSX.Element {
  const [shown, setShown] = useState<ShownIcon>({ iconKey, node: children, outgoing: null, seq: 0 })
  if (iconKey !== shown.iconKey) {
    setShown({
      iconKey,
      node: children,
      outgoing: shown.node,
      seq: shown.seq + 1
    })
  } else if (children !== shown.node) {
    setShown({ ...shown, node: children })
  }

  return (
    <span className="dc-tool-glyph-slot">
      {shown.outgoing ? (
        <span
          key={`out-${shown.seq}`}
          className="dc-tool-glyph-outgoing"
          aria-hidden
          onAnimationEnd={(event) => {
            if (event.target === event.currentTarget) {
              setShown((current) => current.seq === shown.seq ? { ...current, outgoing: null } : current)
            }
          }}
        >
          {shown.outgoing}
        </span>
      ) : null}
      <span
        key={`in-${shown.seq}`}
        className="dc-tool-glyph-incoming"
        data-handoff={shown.seq > 0 ? '' : undefined}
      >
        {children}
      </span>
    </span>
  )
}
