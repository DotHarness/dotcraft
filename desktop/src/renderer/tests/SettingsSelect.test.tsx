import { fireEvent, render, screen } from '@testing-library/react'
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

  it('positions compact upward menus next to the trigger instead of using the max height offset', () => {
    const originalInnerHeight = window.innerHeight
    Object.defineProperty(window, 'innerHeight', {
      configurable: true,
      value: 437
    })
    const rectSpy = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect')
    rectSpy.mockImplementation(function getBoundingClientRect(this: HTMLElement) {
      if (this.getAttribute('role') === 'combobox') {
        return {
          x: 115,
          y: 312,
          top: 312,
          right: 829,
          bottom: 347,
          left: 115,
          width: 714,
          height: 35,
          toJSON: () => {}
        } as DOMRect
      }
      if (this.getAttribute('role') === 'listbox') {
        return {
          x: 115,
          y: 224,
          top: 224,
          right: 829,
          bottom: 306,
          left: 115,
          width: 714,
          height: 82,
          toJSON: () => {}
        } as DOMRect
      }
      return {
        x: 0,
        y: 0,
        top: 0,
        right: 0,
        bottom: 0,
        left: 0,
        width: 0,
        height: 0,
        toJSON: () => {}
      } as DOMRect
    })

    try {
      render(
        <SettingsSelect
          ariaLabel="Model"
          value="deepseek-v4-flash"
          onValueChange={vi.fn()}
          options={[
            { value: 'deepseek-v4-flash', label: 'deepseek-v4-flash' },
            { value: 'deepseek-v4-pro', label: 'deepseek-v4-pro' }
          ]}
        />
      )

      fireEvent.click(screen.getByRole('combobox', { name: 'Model' }))

      const listbox = screen.getByRole('listbox', { name: 'Model' })
      expect(listbox).toHaveStyle({ top: '224px' })
      expect(listbox).not.toHaveStyle({ top: '26px' })
    } finally {
      rectSpy.mockRestore()
      Object.defineProperty(window, 'innerHeight', {
        configurable: true,
        value: originalInnerHeight
      })
    }
  })
})
