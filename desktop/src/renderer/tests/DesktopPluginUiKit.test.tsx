import './setupPluginRuntime'
import { SegmentedControl } from '@dotcraft/plugin'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

describe('Desktop Plugin UI kit', () => {
  it('renders the Core segmented control and reports through the kit onValueChange name', () => {
    const onValueChange = vi.fn()
    render(
      <SegmentedControl<'system' | 'dark'>
        value="system"
        options={[
          { value: 'system', label: 'System' },
          { value: 'dark', label: 'Dark' }
        ]}
        onValueChange={onValueChange}
        ariaLabel="Theme"
      />
    )

    expect(screen.getByRole('group', { name: 'Theme' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'System' })).toHaveAttribute('aria-pressed', 'true')

    fireEvent.click(screen.getByRole('button', { name: 'Dark' }))
    expect(onValueChange).toHaveBeenCalledWith('dark')

    fireEvent.click(screen.getByRole('button', { name: 'System' }))
    expect(onValueChange).toHaveBeenCalledOnce()
  })
})
