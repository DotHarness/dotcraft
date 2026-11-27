import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { DetailPanelTab } from '../components/layout/DetailPanelTab'

describe('DetailPanelTab', () => {
  it('closes without activating the tab and keeps the close button outside the tab button', () => {
    const onActivate = vi.fn()
    const onClose = vi.fn()

    render(
      <div role="tablist">
        <DetailPanelTab
          tabKey="system:changes"
          active
          title="Changes"
          icon={<span />}
          label="Changes"
          badge={3}
          closeLabel="Close Changes"
          onActivate={onActivate}
          onClose={onClose}
          onContextMenu={() => undefined}
        />
      </div>
    )

    const tab = screen.getByRole('tab', { name: /Changes/ })
    const close = screen.getByRole('button', { name: 'Close Changes' })
    expect(tab.contains(close)).toBe(false)
    expect(close.tabIndex).toBe(0)

    fireEvent.click(close)
    expect(onClose).toHaveBeenCalledTimes(1)
    expect(onActivate).not.toHaveBeenCalled()
  })

  it('supports keyboard navigation, Delete to close, and middle-click close', () => {
    const onActivateSecond = vi.fn()
    const onCloseFirst = vi.fn()
    const onCloseSecond = vi.fn()

    render(
      <div role="tablist">
        <DetailPanelTab
          tabKey="system:plan"
          active
          title="Plan"
          icon={<span />}
          label="Plan"
          closeLabel="Close Plan"
          onActivate={() => undefined}
          onClose={onCloseFirst}
          onContextMenu={() => undefined}
        />
        <DetailPanelTab
          tabKey="viewer:file-1"
          active={false}
          title="README.md"
          icon={<span />}
          label="README.md"
          closeLabel="Close README.md"
          onActivate={onActivateSecond}
          onClose={onCloseSecond}
          onContextMenu={() => undefined}
        />
      </div>
    )

    const firstTab = screen.getByRole('tab', { name: 'Plan' })
    const secondTab = screen.getByRole('tab', { name: 'README.md' })
    expect(screen.getByRole('button', { name: 'Close README.md' }).tabIndex).toBe(-1)

    fireEvent.keyDown(firstTab, { key: 'ArrowRight' })
    expect(onActivateSecond).toHaveBeenCalledTimes(1)

    fireEvent.keyDown(firstTab, { key: 'Delete' })
    expect(onCloseFirst).toHaveBeenCalledTimes(1)

    fireEvent(secondTab, new MouseEvent('auxclick', { bubbles: true, button: 1 }))
    expect(onCloseSecond).toHaveBeenCalledTimes(1)
  })
})
