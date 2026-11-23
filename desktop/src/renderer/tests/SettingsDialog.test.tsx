import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { SettingsDialog } from '../components/settings/SettingsDialog'
import { LayerBoundary } from '../contexts/LayerContext'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useUIStore } from '../stores/uiStore'

function renderDialog(nestedLayer = false) {
  return render(
    <LocaleProvider loadSettings={false}>
      <SettingsDialog navigation={<button type="button">Nav</button>}>
        {nestedLayer ? <LayerBoundary><input aria-label="Nested field" /></LayerBoundary> : <input aria-label="Field" />}
      </SettingsDialog>
    </LocaleProvider>
  )
}

describe('SettingsDialog', () => {
  beforeEach(() => {
    useUIStore.setState({ activeMainView: 'conversation', settingsOpen: true })
  })

  it('is a modal dialog that takes focus and returns it to the opener', () => {
    const opener = document.createElement('button')
    document.body.appendChild(opener)
    opener.focus()

    const view = renderDialog()
    const dialog = screen.getByRole('dialog', { name: 'Settings' })
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(dialog).toHaveFocus()

    view.unmount()
    expect(opener).toHaveFocus()
    opener.remove()
  })

  it('closes on Escape and from the close button', () => {
    renderDialog()
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(useUIStore.getState().settingsOpen).toBe(false)

    useUIStore.setState({ settingsOpen: true })
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(useUIStore.getState().settingsOpen).toBe(false)
  })

  it('leaves Escape to a layer opened inside it', () => {
    renderDialog(true)
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(useUIStore.getState().settingsOpen).toBe(true)
  })

  it('keeps Escape for a field that handled it', () => {
    renderDialog()
    const field = screen.getByLabelText('Field')
    field.addEventListener('keydown', (event) => event.preventDefault())
    fireEvent.keyDown(field, { key: 'Escape' })
    expect(useUIStore.getState().settingsOpen).toBe(true)
  })
})
