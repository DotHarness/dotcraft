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
  it('puts both segments in the toolbar band with one shared intent', () => {
    render(
      <SplitButton
        label="Create"
        onClick={vi.fn()}
        items={items()}
        menuLabel="More create options"
        variant="secondary"
      />
    )
    const primary = screen.getByRole('button', { name: 'Create' })
    const menu = screen.getByRole('button', { name: 'More create options' })
    for (const segment of [primary, menu]) {
      expect(segment).toHaveAttribute('data-size', 'toolbar')
      expect(segment).toHaveAttribute('data-variant', 'secondary')
    }
  })

  // The segments meet flush; only hover reveals the seam. A painted divider on the
  // touching edge is not the treatment DESIGN.md specifies.
  it('paints no divider between the segments', () => {
    render(<SplitButton label="Create" onClick={vi.fn()} items={items()} menuLabel="More" />)
    expect(screen.getByRole('button', { name: 'Create' }).style.borderRightWidth).toBe('0px')
    expect(screen.getByRole('button', { name: 'More' }).style.borderLeftWidth).toBe('0px')
  })

  it('names an icon-only principal segment from ariaLabel', () => {
    render(
      <SplitButton
        ariaLabel="Open in File Explorer"
        icon={<span data-testid="glyph" />}
        onClick={vi.fn()}
        items={items()}
        menuLabel="Choose how to open"
      />
    )
    expect(screen.getByRole('button', { name: 'Open in File Explorer' })).toBeInTheDocument()
    expect(screen.getByTestId('glyph')).toBeInTheDocument()
  })

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
