import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { SplitButton, type SplitButtonItem } from '../components/ui/SplitButton'

function items(overrides: Partial<SplitButtonItem>[] = []): SplitButtonItem[] {
  const base: SplitButtonItem[] = [
    { key: 'a', label: 'First', onClick: vi.fn() },
    { key: 'b', label: 'Second', onClick: vi.fn() }
  ]
  return base.map((item, index) => ({ ...item, ...overrides[index] }))
}

describe('SplitButton', () => {
  // The segments meet flush; only hover reveals the seam. A painted divider on the
  // touching edge is not the treatment DESIGN.md specifies.
  it('opens the menu highlighted on the current choice, not the first item', () => {
    const onFirst = vi.fn()
    const onSecond = vi.fn()
    render(
      <SplitButton
        label="Open"
        onClick={vi.fn()}
        items={items([{ onClick: onFirst }, { onClick: onSecond, selected: true }])}
        menuLabel="Choose how to open"
      />
    )
    fireEvent.click(screen.getByRole('button', { name: 'Choose how to open' }))
    fireEvent.keyDown(window, { key: 'Enter' })

    expect(onSecond).toHaveBeenCalledOnce()
    expect(onFirst).not.toHaveBeenCalled()
  })

  it('runs an item and returns focus to the menu segment', async () => {
    const onClick = vi.fn()
    render(
      <SplitButton
        label="Create"
        onClick={vi.fn()}
        items={items([{ onClick }])}
        menuLabel="More"
      />
    )
    const trigger = screen.getByRole('button', { name: 'More' })
    fireEvent.click(trigger)
    fireEvent.click(screen.getByRole('menuitem', { name: 'First' }))

    expect(onClick).toHaveBeenCalledOnce()
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    await vi.waitFor(() => expect(trigger).toHaveFocus())
  })

  it('disables the menu segment when there is nothing to choose', () => {
    render(<SplitButton label="Create" onClick={vi.fn()} items={[]} menuLabel="More" />)
    expect(screen.getByRole('button', { name: 'More' })).toBeDisabled()
  })
})
