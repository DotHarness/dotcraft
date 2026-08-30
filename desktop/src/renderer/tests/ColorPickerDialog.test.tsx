// @vitest-environment jsdom
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import {
  ColorPickerDialog,
  ColorPickerDialogHost,
  requestColorPickerDialog
} from '../components/ui/ColorPickerDialog'
import { installDesktopApiMock } from './desktopApiMock'

beforeEach(() => {
  installDesktopApiMock({ settings: { get: vi.fn().mockResolvedValue({}) } })
})

function renderHost(trigger?: JSX.Element): void {
  render(
    <LocaleProvider>
      {trigger}
      <ColorPickerDialogHost />
    </LocaleProvider>
  )
}

describe('ColorPickerDialog', () => {
  it('normalizes three-digit Hex input and returns the selection', async () => {
    renderHost()
    let request!: ReturnType<typeof requestColorPickerDialog>
    act(() => {
      request = requestColorPickerDialog({
        title: 'Choose accent',
        initialColor: '#4566cc'
      })
    })

    fireEvent.change(screen.getByRole('textbox', { name: 'Hex color' }), {
      target: { value: 'AbC' }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Done' }))

    await expect(request.result).resolves.toEqual({ kind: 'select', color: '#aabbcc' })
  })

  it('keeps invalid input visible and disables Done', () => {
    render(
      <LocaleProvider>
        <ColorPickerDialog
          options={{ title: 'Choose accent', initialColor: '#4566cc' }}
          initialDraft="#12XZ"
          onFinish={vi.fn()}
        />
      </LocaleProvider>
    )

    expect(screen.getByRole('textbox', { name: 'Hex color' })).toHaveValue('#12XZ')
    expect(screen.getByRole('alert')).toHaveTextContent('Enter a 3- or 6-digit hex color.')
    expect(screen.getByRole('button', { name: 'Done' })).toBeDisabled()
  })

  it('resets immediately and exposes localized slider names', async () => {
    renderHost()
    let request!: ReturnType<typeof requestColorPickerDialog>
    act(() => {
      request = requestColorPickerDialog({
        title: 'Choose accent',
        initialColor: '#4566cc',
        allowReset: true,
        defaultColor: '#4566cc'
      })
    })

    expect(screen.getByRole('slider', { name: 'Saturation and brightness' })).toBeInTheDocument()
    expect(screen.getByRole('slider', { name: 'Hue' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Reset to default' }))

    await expect(request.result).resolves.toEqual({ kind: 'reset' })
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('keeps keyboard focus inside the dialog and lets arrow keys adjust Hue', () => {
    render(
      <LocaleProvider>
        <ColorPickerDialog
          options={{ title: 'Choose accent', initialColor: '#4566cc' }}
          onFinish={vi.fn()}
        />
      </LocaleProvider>
    )
    const input = screen.getByRole('textbox', { name: 'Hex color' })
    const hue = screen.getByRole('slider', { name: 'Hue' })
    const close = screen.getByRole('button', { name: 'Close' })
    const done = screen.getByRole('button', { name: 'Done' })
    const before = (input as HTMLInputElement).value

    fireEvent.keyDown(hue, { key: 'ArrowRight', keyCode: 39, which: 39 })
    expect(input).not.toHaveValue(before)

    done.focus()
    fireEvent.keyDown(document, { key: 'Tab' })
    expect(close).toHaveFocus()
    close.focus()
    fireEvent.keyDown(document, { key: 'Tab', shiftKey: true })
    expect(done).toHaveFocus()
  })

  it('omits reset, alpha, and eyedropper controls when reset is unavailable', () => {
    render(
      <LocaleProvider>
        <ColorPickerDialog
          options={{ title: 'Choose label color', initialColor: '#3e8c64' }}
          onFinish={vi.fn()}
        />
      </LocaleProvider>
    )
    expect(screen.queryByRole('button', { name: 'Reset to default' })).toBeNull()
    expect(screen.queryByLabelText(/alpha|opacity|eyedropper/i)).toBeNull()
  })

  it.each([
    ['Escape', () => fireEvent.keyDown(document, { key: 'Escape' })],
    ['close', () => fireEvent.click(screen.getByRole('button', { name: 'Close' }))],
    ['backdrop', () => fireEvent.mouseDown(screen.getByRole('dialog'))]
  ])('treats %s as cancel', async (_name, cancel) => {
    renderHost()
    let request!: ReturnType<typeof requestColorPickerDialog>
    act(() => {
      request = requestColorPickerDialog({ title: 'Choose accent', initialColor: '#4566cc' })
    })
    cancel()
    await expect(request.result).resolves.toEqual({ kind: 'cancel' })
  })

  it('cancels a second request and restores the original trigger focus', async () => {
    renderHost(<button type="button">Open picker</button>)
    const trigger = screen.getByRole('button', { name: 'Open picker' })
    trigger.focus()
    let first!: ReturnType<typeof requestColorPickerDialog>
    let second!: ReturnType<typeof requestColorPickerDialog>
    act(() => {
      first = requestColorPickerDialog({ title: 'First', initialColor: '#4566cc' })
      second = requestColorPickerDialog({ title: 'Second', initialColor: '#abcdef' })
    })

    await expect(second.result).resolves.toEqual({ kind: 'cancel' })
    expect(screen.getByRole('dialog', { name: 'First' })).toBeInTheDocument()
    act(() => first.dismiss())
    await expect(first.result).resolves.toEqual({ kind: 'cancel' })
    await waitFor(() => expect(trigger).toHaveFocus())
  })

  it('rejects invalid Host options with TypeError', () => {
    expect(() => requestColorPickerDialog({
      title: 'Choose accent',
      initialColor: 'not-a-color'
    })).toThrow(TypeError)
  })
})
