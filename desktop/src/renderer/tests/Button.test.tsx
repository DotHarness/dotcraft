import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Button, ButtonLabel } from '../components/ui/Button'
import { IconButton } from '../components/ui/IconButton'

describe('Button', () => {
  it('preserves a compound accessible label and both icons during loading', () => {
    const onClick = vi.fn()
    const content = <><ButtonLabel>Export <strong>report</strong></ButtonLabel><svg aria-hidden data-testid="trailing" /></>
    const view = render(<Button iconLeft={<svg aria-hidden data-testid="leading" />} onClick={onClick}>{content}</Button>)
    screen.getByRole('button', { name: 'Export report' }).click()
    expect(onClick).toHaveBeenCalledTimes(1)
    view.rerender(<Button loading iconLeft={<svg aria-hidden data-testid="leading" />} onClick={onClick}>{content}</Button>)
    const button = screen.getByRole('button', { name: 'Export report' })
    expect(button).toBeDisabled()
    expect(button).toHaveAttribute('aria-busy', 'true')
    expect(screen.getByTestId('leading')).toBeInTheDocument()
    expect(screen.getByTestId('trailing')).toBeInTheDocument()
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

})
