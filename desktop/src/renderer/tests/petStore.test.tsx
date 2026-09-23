import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { DEFAULT_PET_SETTINGS } from '../../shared/pet'
import { FIND_RUNTIME_MS, FIND_TOKEN_GOAL } from '../pet/petModel'
import { usePetStore } from '../pet/petStore'

const originalApi = window.api
let settingsSet: ReturnType<typeof vi.fn>

beforeEach(() => {
  settingsSet = vi.fn(async () => {})
  window.api = { settings: { set: settingsSet } } as unknown as typeof window.api
})

afterEach(() => {
  window.api = originalApi
})

describe('pet finds', () => {
  it('needs both gates, then adds one item, resets both counters, and persists', () => {
    usePetStore.getState().hydrate({ pet: DEFAULT_PET_SETTINGS })
    expect(usePetStore.getState().advance(FIND_RUNTIME_MS, 0)).toBeNull()
    expect(usePetStore.getState().advance(0, FIND_TOKEN_GOAL - 1)).toBeNull()
    const found = usePetStore.getState().advance(0, 1)
    expect(found).not.toBeNull()
    const { settings } = usePetStore.getState()
    expect(settings.bag[found!]).toBe(1)
    expect(settings.drops).toMatchObject({ runtimeMs: 0, tokens: 0, found: 1, last: found })
    expect(settingsSet).toHaveBeenLastCalledWith({ pet: settings })
  })

  it('pauses both counters and never finds while customization is off', () => {
    usePetStore.getState().hydrate({ pet: { ...DEFAULT_PET_SETTINGS, customization: false } })
    expect(usePetStore.getState().advance(FIND_RUNTIME_MS, FIND_TOKEN_GOAL)).toBeNull()
    expect(usePetStore.getState().settings.drops).toMatchObject({ runtimeMs: 0, tokens: 0, found: 0 })
    expect(settingsSet).not.toHaveBeenCalled()
  })
})
