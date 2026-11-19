import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ModelPicker } from '../components/conversation/ModelPicker'
import { installDesktopApiMock } from './desktopApiMock'

function openMenu(withProviders: boolean): void {
  render(
    <LocaleProvider loadSettings={false}>
      <ModelPicker
        modelName="gpt-new"
        modelOptions={['gpt-new', 'gpt-old']}
        allowDefaultModel={false}
        triggerStyle={{}}
        {...(withProviders
          ? {
              providerId: 'chatgpt',
              providerOptions: [{ id: 'chatgpt', displayName: 'ChatGPT' }, { id: 'openai', displayName: 'OpenAI' }],
              onProviderChange: vi.fn()
            }
          : {})}
      />
    </LocaleProvider>
  )
  fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
  fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: /gpt-new/ }))
}

function rowLabels(): string[] {
  return within(screen.getByRole('menu')).getAllByRole('menuitem').map((row) => row.textContent ?? '')
}

beforeEach(() => {
  installDesktopApiMock({ initialLocale: 'en' })
})

describe('ModelPicker menu', () => {
  it('lists Model above Provider and focuses Model when the menu opens', async () => {
    openMenu(true)

    const labels = rowLabels()
    expect(labels).toHaveLength(3)
    expect(labels[1]).toMatch(/^Model/)
    expect(labels[2]).toMatch(/^Provider/)
    const rows = within(screen.getByRole('menu')).getAllByRole('menuitem')
    await waitFor(() => expect(document.activeElement).toBe(rows[1]))

    fireEvent.keyDown(document, { key: 'ArrowDown' })
    expect(document.activeElement).toBe(rows[2])
    fireEvent.keyDown(document, { key: 'ArrowUp' })
    fireEvent.keyDown(document, { key: 'ArrowRight' })
    const options = within(screen.getByRole('listbox', { name: 'Model' })).getAllByRole('option')
    expect(options.map((option) => option.textContent)).toEqual(['gpt-new', 'gpt-old'])
  })

  it('focuses Model when no provider row is shown', async () => {
    openMenu(false)

    const rows = within(screen.getByRole('menu')).getAllByRole('menuitem')
    expect(rows).toHaveLength(2)
    expect(rows[1].textContent).toMatch(/^Model/)
    await waitFor(() => expect(document.activeElement).toBe(rows[1]))
  })
})
