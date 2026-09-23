import { create } from 'zustand'
import { itemOf, type Appearance, type ItemId, type Slot } from '@dotcraft/avatar'
import { DEFAULT_PET_SETTINGS, type PetSettings } from '../../shared/pet'
import {
  FIND_RUNTIME_MS, appearanceOf, clearSlot, exchangeItems, findReady, recordFind, rollExchange, rollFind, sanitizePetSettings, trayValid, wearItem,
  type Random
} from './petModel'

/** Each tick adds exactly this much, so time asleep or suspended never counts toward a find. */
export const PET_CLOCK_MS = 60_000

interface PetState {
  settings: PetSettings
  appearance: Appearance
  hydrated: boolean
  hydrate(raw: { pet?: PetSettings }): void
  setCustomization(on: boolean): void
  setPalette(palette: number): void
  wear(id: ItemId): void
  takeOff(slot: Slot): void
  exchange(tray: ItemId[]): ItemId | null
  advance(runtimeMs: number, tokens: number, random?: Random): ItemId | null
}

export const usePetStore = create<PetState>((set, get) => {
  const commit = (settings: PetSettings): void => {
    set({ settings, appearance: appearanceOf(settings) })
    void window.api.settings.set({ pet: settings })
  }
  return {
    settings: DEFAULT_PET_SETTINGS,
    appearance: appearanceOf(DEFAULT_PET_SETTINGS),
    hydrated: false,
    hydrate(raw) {
      const settings = sanitizePetSettings(raw.pet ?? DEFAULT_PET_SETTINGS)
      set({ settings, appearance: appearanceOf(settings), hydrated: true })
    },
    setCustomization(on) { commit({ ...get().settings, customization: on }) },
    setPalette(palette) { commit({ ...get().settings, palette }) },
    wear(id) { commit(wearItem(get().settings, id)) },
    takeOff(slot) { commit(clearSlot(get().settings, slot)) },
    exchange(tray) {
      const settings = get().settings
      if (!trayValid(settings.bag, settings.outfit, tray)) return null
      const id = rollExchange(itemOf(tray[0]).rarity, Math.random)
      if (!id) return null
      commit(exchangeItems(settings, tray, id))
      return id
    },
    advance(runtimeMs, tokens, random = Math.random) {
      const settings = get().settings
      if (!get().hydrated || !settings.customization) return null
      const drops = {
        ...settings.drops,
        runtimeMs: Math.min(FIND_RUNTIME_MS, settings.drops.runtimeMs + runtimeMs),
        tokens: settings.drops.tokens + tokens
      }
      if (!findReady(drops)) {
        // Token deltas stream in constantly; the minute tick is what writes progress to disk.
        const next = { ...settings, drops }
        if (runtimeMs > 0) commit(next)
        else set({ settings: next })
        return null
      }
      const id = rollFind(random)
      commit(recordFind({ ...settings, drops }, id))
      return id
    }
  }
})
