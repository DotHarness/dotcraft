import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Button } from '../components/ui/Button'

describe('Button', () => {
  it('applies variant and size as data attributes on a .dc-button', () => {
    render(<Button variant="primary" size="default">Save</Button>)
    const button = screen.getByRole('button', { name: 'Save' })
    expect(button).toHaveClass('dc-button')
    expect(button).toHaveAttribute('data-variant', 'primary')
    expect(button).toHaveAttribute('data-size', 'default')
    expect(button).toHaveAttribute('type', 'button')
  })

  it('defaults to a secondary, default-size button', () => {
    render(<Button>Manage</Button>)
    const button = screen.getByRole('button', { name: 'Manage' })
    expect(button).toHaveAttribute('data-variant', 'secondary')
    expect(button).toHaveAttribute('data-size', 'default')
  })

  it('renders a leading icon before the label for text sizes', () => {
    render(
      <Button iconLeft={<svg data-testid="plus" />}>Add provider</Button>
    )
    expect(screen.getByTestId('plus')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add provider' })).toBeInTheDocument()
  })

  it('disables the button and hides the label glyph while loading for icon sizes', () => {
    render(
      <Button size="icon" loading aria-label="Refresh">
        <svg data-testid="glyph" />
      </Button>
    )
    const button = screen.getByRole('button', { name: 'Refresh' })
    expect(button).toBeDisabled()
    expect(screen.queryByTestId('glyph')).toBeNull()
  })

  it('merges a caller className and forwards click handlers', () => {
    const onClick = vi.fn()
    render(
      <Button className="extra" onClick={onClick}>Run now</Button>
    )
    const button = screen.getByRole('button', { name: 'Run now' })
    expect(button).toHaveClass('dc-button')
    expect(button).toHaveClass('extra')
    button.click()
    expect(onClick).toHaveBeenCalledTimes(1)
  })
})
