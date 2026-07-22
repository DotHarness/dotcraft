import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { Combobox } from '../components/ui/Combobox'

function Harness(): JSX.Element {
  const [value, setValue] = useState('')
  return (
    <Combobox
      value={value}
      onValueChange={setValue}
      ariaLabel="Category"
      options={[
        { value: 'Engineering', label: 'Engineering' },
        { value: 'Operations', label: 'Operations' }
      ]}
    />
  )
}

describe('Combobox', () => {
  it('allows free text and filters selectable suggestions', () => {
    render(<Harness />)

    const input = screen.getByRole('combobox', { name: 'Category' })
    fireEvent.change(input, { target: { value: 'engi' } })

    expect(input).toHaveValue('engi')
    expect(screen.getByRole('option', { name: 'Engineering' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'Operations' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('option', { name: 'Engineering' }))

    expect(input).toHaveValue('Engineering')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('supports keyboard selection', () => {
    render(<Harness />)

    const input = screen.getByRole('combobox', { name: 'Category' })
    fireEvent.focus(input)
    fireEvent.keyDown(input, { key: 'ArrowDown' })
    fireEvent.keyDown(input, { key: 'Enter' })

    expect(input).toHaveValue('Operations')
  })
})
