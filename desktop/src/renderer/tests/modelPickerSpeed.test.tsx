import { fireEvent, render, screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ModelPicker } from '../components/conversation/ModelPicker'
import type { InferenceSpeedWire, ModelCatalogItem } from '../stores/modelCatalogStore'
import { installDesktopApiMock } from './desktopApiMock'

function model(supportedModes: InferenceSpeedWire[]): ModelCatalogItem {
  return { id: 'gpt-test', speed: { supportedModes, defaultMode: 'standard' } }
}

function openPicker(catalog: ModelCatalogItem, speedValue: InferenceSpeedWire, onSpeedChange = vi.fn()) {
  render(
    <LocaleProvider loadSettings={false}>
      <ModelPicker
        modelName={catalog.id}
        modelOptions={[catalog.id]}
        modelCatalog={[catalog]}
        speedValue={speedValue}
        onSpeedChange={onSpeedChange}
        triggerStyle={{}}
      />
    </LocaleProvider>
  )
  fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
  return onSpeedChange
}

beforeEach(() => {
  installDesktopApiMock({ initialLocale: 'en' })
})

describe('ModelPicker speed control', () => {
  it('toggles Fast when the model does not advertise Ultrafast', () => {
    const onSpeedChange = openPicker(model(['standard', 'fast']), 'fast')

    const toggle = screen.getByRole('button', { name: 'Fast' })
    expect(toggle.getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(toggle)
    expect(onSpeedChange).toHaveBeenCalledWith('standard')
  })

  it('offers every advertised speed in a flyout and keeps the panel open after selection', () => {
    const onSpeedChange = openPicker(model(['standard', 'fast', 'ultrafast']), 'fast')

    fireEvent.click(screen.getByRole('button', { name: 'Speed: Fast' }))
    const options = within(screen.getByRole('listbox', { name: 'Speed' })).getAllByRole('option')
    expect(options.map((option) => option.getAttribute('aria-selected'))).toEqual(['false', 'true', 'false'])
    fireEvent.click(options[2])
    expect(onSpeedChange).toHaveBeenCalledWith('ultrafast')
    expect(screen.queryByRole('listbox', { name: 'Speed' })).toBeNull()
    expect(screen.getByRole('dialog')).toBeTruthy()
  })

  it('opens the flyout for a model that advertises only Ultrafast', () => {
    openPicker(model(['standard', 'ultrafast']), 'standard')

    fireEvent.click(screen.getByRole('button', { name: 'Speed: Standard' }))
    expect(within(screen.getByRole('listbox', { name: 'Speed' })).getAllByRole('option')).toHaveLength(2)
  })

  it('treats a stored speed the model does not advertise as Standard', () => {
    openPicker(model(['standard', 'fast']), 'ultrafast')

    expect(screen.getByRole('button', { name: 'Fast' }).getAttribute('aria-pressed')).toBe('false')
  })
})
