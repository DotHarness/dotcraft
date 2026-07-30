import { useState, type ReactNode } from 'react'
import { DragHandle } from '../layout/DragHandle'

interface ExplorerDockProps {
  width: number
  onDrag: (delta: number) => void
  children: ReactNode
}

/**
 * Shared dock shell for file-list side panels.
 *
 * The visible one-pixel edge belongs to the panel. The wider resize target is
 * overlaid on that edge so it never consumes layout width or creates a gap.
 */
export function ExplorerDock({
  width,
  onDrag,
  children
}: ExplorerDockProps): JSX.Element {
  const [dividerActive, setDividerActive] = useState(false)

  return (
    <div
      data-testid="explorer-dock"
      style={{
        position: 'relative',
        flex: `0 1 ${width}px`,
        minWidth: 140,
        display: 'flex',
        flexDirection: 'column'
      }}
    >
      <div
        style={{
          minWidth: 0,
          minHeight: 0,
          flex: 1,
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column',
          borderLeft: '1px solid var(--glass-border)'
        }}
      >
        {children}
      </div>
      <DragHandle
        onDrag={onDrag}
        onActiveChange={setDividerActive}
        className="explorer-dock__drag-handle"
        style={{
          position: 'absolute',
          top: 0,
          bottom: 0,
          left: 'calc(var(--resize-divider-hit-width) / -2)'
        }}
      />
      <div
        aria-hidden
        data-testid="explorer-divider-glow"
        style={{
          position: 'absolute',
          top: 0,
          bottom: 0,
          left: 0,
          width: 'var(--main-surface-edge-glow-width)',
          background: 'var(--main-surface-edge-glow)',
          opacity: dividerActive ? 1 : 0,
          transition: 'opacity 150ms ease',
          pointerEvents: 'none',
          zIndex: 4
        }}
      />
    </div>
  )
}
