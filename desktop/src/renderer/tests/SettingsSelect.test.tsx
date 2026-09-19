import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { SettingsSelect } from '../components/settings/ui/SettingsSelect'

describe('SettingsSelect', () => {
  it('opens the custom listbox and selects an option', () => {
    const onValueChange = vi.fn()

    render(
      <SettingsSelect
        ariaLabel="Mode"
        value="standard"
        onValueChange={onValueChange}
        options={[
          { value: 'standard', label: 'Standard', description: 'Balanced speed' },
          { value: 'fast', label: 'Fast', description: 'Higher throughput' }
        ]}
      />
    )

    fireEvent.click(screen.getByRole('combobox', { name: 'Mode' }))

    expect(screen.getByRole('listbox', { name: 'Mode' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: /Standard/ })).toHaveAttribute('aria-selected', 'true')

    fireEvent.click(screen.getByRole('option', { name: /Fast/ }))

    expect(onValueChange).toHaveBeenCalledWith('fast')
    expect(screen.queryByRole('listbox', { name: 'Mode' })).not.toBeInTheDocument()
  })

  it('keeps the menu open when a value change is rejected', () => {
    render(
      <SettingsSelect
        ariaLabel="Mode"
        value="standard"
        onValueChange={() => false}
        options={[
          { value: 'standard', label: 'Standard' },
          { value: 'fast', label: 'Fast' }
        ]}
      />
    )

    fireEvent.click(screen.getByRole('combobox', { name: 'Mode' }))
    fireEvent.click(screen.getByRole('option', { name: 'Fast' }))

    expect(screen.getByRole('listbox', { name: 'Mode' })).toBeInTheDocument()
  })

  it('supports keyboard navigation', () => {
    const onValueChange = vi.fn()

    render(
      <SettingsSelect
        ariaLabel="Mode"
        value="standard"
        onValueChange={onValueChange}
        options={[
          { value: 'standard', label: 'Standard' },
          { value: 'fast', label: 'Fast' },
          { value: 'careful', label: 'Careful' }
        ]}
      />
    )

    const combobox = screen.getByRole('combobox', { name: 'Mode' })
    fireEvent.keyDown(combobox, { key: 'ArrowDown' })
    fireEvent.keyDown(combobox, { key: 'ArrowDown' })
    fireEvent.keyDown(combobox, { key: 'Enter' })

    expect(onValueChange).toHaveBeenCalledWith('fast')
  })

  it('waits for an asynchronous pre-open check', async () => {
    let resolveAccess: ((allowed: boolean) => void) | undefined
    const onBeforeOpen = vi.fn(() => new Promise<boolean>((resolve) => { resolveAccess = resolve }))
    render(
      <SettingsSelect
        ariaLabel="Microphone"
        value="default"
        onBeforeOpen={onBeforeOpen}
        onValueChange={vi.fn()}
        options={[{ value: 'default', label: 'System default' }]}
      />
    )

    fireEvent.click(screen.getByRole('combobox', { name: 'Microphone' }))
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    resolveAccess?.(true)
    await waitFor(() => expect(screen.getByRole('listbox', { name: 'Microphone' })).toBeInTheDocument())
  })

  it('keeps the menu closed when the pre-open check is rejected', async () => {
    render(
      <SettingsSelect
        ariaLabel="Microphone"
        value="default"
        onBeforeOpen={async () => false}
        onValueChange={vi.fn()}
        options={[{ value: 'default', label: 'System default' }]}
      />
    )

    fireEvent.click(screen.getByRole('combobox', { name: 'Microphone' }))
    await Promise.resolve()
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

})
