import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { items, type ItemId } from '@dotcraft/avatar'
import { PetExchangeGroup } from '../components/settings/panels/pet/PetExchangeGroup'
import { LocaleProvider } from '../contexts/LocaleContext'
import { usePetStore } from '../pet/petStore'
import { useToastStore } from '../stores/toastStore'
import { DEFAULT_PET_SETTINGS } from '../../shared/pet'

const originalApi = window.api
const commons = items.filter((item) => item.rarity === 'common').slice(0, 2).map((item) => item.id as ItemId)

beforeEach(() => {
  window.api = { settings: { set: vi.fn(async () => {}), get: vi.fn(async () => ({ locale: 'en' })) } } as unknown as typeof window.api
  usePetStore.getState().hydrate({ pet: { ...DEFAULT_PET_SETTINGS, bag: { [commons[0]]: 7, [commons[1]]: 4 } } })
  for (const toast of useToastStore.getState().toasts) useToastStore.getState().removeToast(toast.id)
})

afterEach(() => {
  window.api = originalApi
})

function renderExchange(): void {
  render(<LocaleProvider><PetExchangeGroup settings={usePetStore.getState().settings} /></LocaleProvider>)
}

describe('pet exchange', () => {
  it('trades the items picked by hand and announces the result as a toast', () => {
    renderExchange()
    const spares = screen.getByRole('list', { name: 'Spares' })
    const [first, second] = within(spares).getAllByRole('listitem')
    for (let copy = 0; copy < 7; copy++) fireEvent.click(first)
    expect(first).toBeDisabled()
    for (let copy = 0; copy < 3; copy++) fireEvent.click(second)
    expect(second).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: /Exchange for one/ }))

    expect(usePetStore.getState().settings.bag[commons[0]]).toBeUndefined()
    expect(usePetStore.getState().settings.bag[commons[1]]).toBe(1)
    expect(useToastStore.getState().toasts).toEqual([expect.objectContaining({ key: 'pet-find', message: expect.stringMatching(/^Exchanged for /) })])
  })
})
