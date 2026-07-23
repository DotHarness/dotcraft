import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Input, Textarea } from '../components/ui/Input'

describe('Input', () => {
  it('carries the shared field class so the focus border rule applies', () => {
    render(<Input aria-label="source" />)
    expect(screen.getByLabelText('source')).toHaveClass('dc-field')
  })

  it('keeps a caller className alongside the shared one', () => {
    render(<Input aria-label="source" className="extra" />)
    const field = screen.getByLabelText('source')
    expect(field).toHaveClass('dc-field')
    expect(field).toHaveClass('extra')
  })

  it('defaults to the bordered shape with no shape modifier set', () => {
    render(<Input aria-label="source" />)
    const field = screen.getByLabelText('source')
    expect(field).not.toHaveAttribute('data-frameless')
    expect(field).not.toHaveAttribute('data-bare')
    expect(field).not.toHaveAttribute('data-invalid')
    expect(field).not.toHaveAttribute('data-mono')
  })

  it('marks the frameless, bare, mono, and toolbar variants', () => {
    render(<Input aria-label="source" frameless bare mono size="toolbar" />)
    const field = screen.getByLabelText('source')
    expect(field).toHaveAttribute('data-frameless')
    expect(field).toHaveAttribute('data-bare')
    expect(field).toHaveAttribute('data-mono')
    expect(field).toHaveAttribute('data-size', 'toolbar')
  })

  it('exposes invalid to assistive technology as well as to the border', () => {
    render(<Input aria-label="source" invalid />)
    const field = screen.getByLabelText('source')
    expect(field).toHaveAttribute('data-invalid')
    expect(field).toHaveAttribute('aria-invalid', 'true')
  })

  // The field owns its height; a caller that set `flex: 1` for a row layout would
  // otherwise collapse the control when the parent is a column.
  it('never sets flex on the element itself', () => {
    render(<Input aria-label="source" />)
    expect(screen.getByLabelText('source').style.flex).toBe('')
  })
})

describe('Textarea', () => {
  it('is always multiline so the height and resize rules apply', () => {
    render(<Textarea aria-label="paths" />)
    const field = screen.getByLabelText('paths')
    expect(field).toHaveClass('dc-field')
    expect(field).toHaveAttribute('data-multiline')
  })
})
