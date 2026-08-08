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

  it('expands text-only choices before revealing the menu and does not use tooltip compensation', () => {
    const rectSpy = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect')
    rectSpy.mockImplementation(function getBoundingClientRect(this: HTMLElement) {
      const width = this.closest('.dc-adaptive-select__measure')
        ? (this.textContent?.length ?? 0) * 8
        : this.getAttribute('role') === 'combobox'
          ? 180
          : 0
      const left = this.getAttribute('role') === 'combobox' ? 300 : 0
      return {
        x: left,
        y: 100,
        top: 100,
        right: left + width,
        bottom: 135,
        left,
        width,
        height: 35,
        toJSON: () => {}
      } as DOMRect
    })

    const fullName = 'Microphone (Razer Kraken V3 X) (1532:0537)'
    try {
      render(
        <SettingsSelect
          ariaLabel="Microphone"
          value="default"
          onValueChange={vi.fn()}
          options={[
            { value: 'default', label: 'System default', tooltip: 'System default' },
            { value: 'razer', label: fullName, tooltip: fullName }
          ]}
        />
      )

      const combobox = screen.getByRole('combobox', { name: 'Microphone' })
      fireEvent.mouseEnter(combobox.parentElement as HTMLElement)
      expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()

      fireEvent.click(combobox)
      const listbox = screen.getByRole('listbox', { name: 'Microphone' })
      expect(listbox).toHaveAttribute('data-adaptive-select-ready', 'false')
      expect(listbox.style.width).toBe('408px')

      fireEvent.transitionEnd(combobox, { propertyName: 'width' })
      expect(listbox).toHaveAttribute('data-adaptive-select-ready', 'true')
    } finally {
      rectSpy.mockRestore()
    }
  })

  it('anchors adaptive menus to the field side', () => {
    const rectSpy = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect')
    rectSpy.mockImplementation(function getBoundingClientRect(this: HTMLElement) {
      const width = this.closest('.dc-adaptive-select__measure')
        ? (this.textContent?.length ?? 0) * 8
        : this.getAttribute('role') === 'combobox' || this.classList.contains('dc-adaptive-select')
          ? 140
          : this.classList.contains('dc-settings-row')
            ? 520
            : 0
      const left = this.classList.contains('dc-settings-row') ? 100 : 480
      return {
        x: left,
        y: 100,
        top: 100,
        right: left + width,
        bottom: 135,
        left,
        width,
        height: 35,
        toJSON: () => {}
      } as DOMRect
    })

    try {
      const { unmount } = render(
        <SettingsSelect
          ariaLabel="Protocol"
          value="responses"
          onValueChange={vi.fn()}
          options={[
            { value: 'responses', label: 'OpenAI-Responses' },
            { value: 'legacy', label: 'OpenAI-Legacy' }
          ]}
        />
      )

      fireEvent.click(screen.getByRole('combobox', { name: 'Protocol' }))
      const leftMenu = screen.getByRole('listbox', { name: 'Protocol' })
      expect(leftMenu).toHaveAttribute('data-adaptive-select-anchor', 'left')
      expect(leftMenu.style.left).toBe('480px')
      unmount()

      render(
        <div className="dc-settings-row">
          <SettingsSelect
            ariaLabel="Frequency"
            value="daily"
            onValueChange={vi.fn()}
            options={[
              { value: 'daily', label: 'Every 24 hours' },
              { value: 'weekly', label: 'Every seven days' }
            ]}
          />
        </div>
      )

      fireEvent.click(screen.getByRole('combobox', { name: 'Frequency' }))
      const rightMenu = screen.getByRole('listbox', { name: 'Frequency' })
      expect(rightMenu).toHaveAttribute('data-adaptive-select-anchor', 'right')
      expect(rightMenu.style.right).toBe('')
      expect(Number.parseFloat(rightMenu.style.left)).toBeLessThan(480)
    } finally {
      rectSpy.mockRestore()
    }
  })

  it('keeps tooltip support and fixed width for rich options', async () => {
    const fullName = 'Microphone (Razer Kraken V3 X) (1532:0537)'
    render(
      <SettingsSelect
        ariaLabel="Microphone"
        value="razer"
        onValueChange={vi.fn()}
        options={[{ value: 'razer', label: fullName, description: 'Current input device', tooltip: fullName }]}
      />
    )

    const combobox = screen.getByRole('combobox', { name: 'Microphone' })
    fireEvent.mouseEnter(combobox.parentElement as HTMLElement)
    expect(await screen.findByRole('tooltip')).toHaveTextContent(fullName)
    fireEvent.mouseLeave(combobox.parentElement as HTMLElement)

    fireEvent.click(combobox)
    expect(screen.getByRole('listbox', { name: 'Microphone' })).not.toHaveAttribute('data-adaptive-select')
  })

  it('keeps frameless toolbar selects fixed width', () => {
    render(
      <SettingsSelect
        ariaLabel="Repository"
        appearance="frameless"
        value="all"
        onValueChange={vi.fn()}
        options={[
          { value: 'all', label: 'All repositories' },
          { value: 'sample', label: 'example-org/sample-project' }
        ]}
      />
    )

    const combobox = screen.getByRole('combobox', { name: 'Repository' })
    expect(combobox.closest('.dc-adaptive-select')).toBeNull()
    fireEvent.click(combobox)
    expect(screen.getByRole('listbox', { name: 'Repository' })).not.toHaveAttribute('data-adaptive-select')
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
