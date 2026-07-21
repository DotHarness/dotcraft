import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Button } from '../components/ui/Button'
import { IconButton } from '../components/ui/IconButton'

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

  it('exposes the prominent size for a single high-emphasis CTA', () => {
    render(<Button size="prominent" variant="primary">Install</Button>)
    expect(screen.getByRole('button', { name: 'Install' })).toHaveAttribute('data-size', 'prominent')
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

describe('IconButton', () => {
  it('forwards refs and exposes danger and expanded states', () => {
    const ref = { current: null as HTMLButtonElement | null }
    render(
      <IconButton
        ref={ref}
        label="Delete item"
        icon={<svg />}
        tone="danger"
        aria-expanded="true"
      />
    )
    const button = screen.getByRole('button', { name: 'Delete item' })
    expect(ref.current).toBe(button)
    expect(button).toHaveClass('dc-icon-button')
    expect(button).toHaveAttribute('data-tone', 'danger')
    expect(button).toHaveAttribute('aria-expanded', 'true')
  })

  it('merges caller classes without dropping shared icon-button behavior', () => {
    render(<IconButton label="Copy" icon={<svg />} className="inline-action" />)
    expect(screen.getByRole('button', { name: 'Copy' })).toHaveClass('dc-icon-button', 'inline-action')
  })

  it('keeps disabled and bordered semantics on the shared control', () => {
    render(<IconButton label="Apps" icon={<svg />} bordered disabled />)
    const button = screen.getByRole('button', { name: 'Apps' })
    expect(button).toBeDisabled()
    expect(button).toHaveAttribute('data-bordered', 'true')
  })
})
