import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ExplorerDock } from '../components/detail/ExplorerDock'

describe('ExplorerDock', () => {
  it('overlays a working resize target on the visible panel edge', () => {
    const onDrag = vi.fn()

    render(
      <div style={{ display: 'flex', width: 800 }}>
        <div data-testid="content" style={{ flex: 1 }} />
        <ExplorerDock width={260} onDrag={onDrag}>
          <div>Explorer</div>
        </ExplorerDock>
      </div>
    )

    const dock = screen.getByTestId('explorer-dock')
    const separator = screen.getByRole('separator')
    const glow = screen.getByTestId('explorer-divider-glow')
    const contentShell = dock.firstElementChild as HTMLElement

    expect(dock.style.position).toBe('relative')
    expect(contentShell.style.borderLeft).toBe('1px solid var(--glass-border)')
    expect(contentShell.style.overflow).toBe('hidden')
    expect(separator).toHaveStyle({
      position: 'absolute',
      left: 'calc(var(--resize-divider-hit-width) / -2)'
    })

    fireEvent.pointerEnter(separator)
    expect(glow).toHaveStyle({ opacity: 1 })

    fireEvent.pointerDown(separator, { clientX: 320 })
    fireEvent.pointerMove(document, { clientX: 348 })
    fireEvent.pointerUp(document)

    expect(onDrag).toHaveBeenCalledWith(28)
  })
})
