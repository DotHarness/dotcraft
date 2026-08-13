interface ResizeEdgeGlowProps {
  /** Hovered or dragged. */
  active: boolean
  testId: string
  /** Offset for edges that start below the chrome header rather than at the top. */
  top?: string
}

/**
 * Every resizable edge lights up the same way, so the highlight lives here
 * rather than being re-declared beside each `DragHandle`. The rest state stays
 * the owning panel's own hairline; this only fades in over it.
 */
export function ResizeEdgeGlow({ active, testId, top = '0' }: ResizeEdgeGlowProps): JSX.Element {
  return (
    <div
      aria-hidden
      data-testid={testId}
      style={{
        position: 'absolute',
        top,
        bottom: 0,
        left: 0,
        width: 'var(--main-surface-edge-glow-width)',
        background: 'var(--main-surface-edge-glow)',
        opacity: active ? 1 : 0,
        transition: 'opacity 150ms ease',
        pointerEvents: 'none',
        zIndex: 4
      }}
    />
  )
}
