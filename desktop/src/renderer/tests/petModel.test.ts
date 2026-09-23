import { describe, expect, it } from 'vitest'
import { itemOf, rarities } from '@dotcraft/avatar'
import { DEFAULT_PET_SETTINGS, type PetSettings } from '../../shared/pet'
import {
  EXCHANGE_COUNT, FIND_RUNTIME_MS, FIND_TOKEN_GOAL, appearanceOf, autoTray, bagTotal, exchangeItems, findReady, rollExchange, rollFind,
  sanitizePetSettings, spareTotal, trayValid, wearItem, wouldClear
} from '../pet/petModel'

function settings(partial: Partial<PetSettings> = {}): PetSettings {
  return { ...DEFAULT_PET_SETTINGS, ...partial }
}

describe('pet finds', () => {
  it('need both the runtime and the token gate', () => {
    expect(findReady({ runtimeMs: FIND_RUNTIME_MS, tokens: FIND_TOKEN_GOAL - 1, found: 0, last: null })).toBe(false)
    expect(findReady({ runtimeMs: FIND_RUNTIME_MS - 1, tokens: FIND_TOKEN_GOAL, found: 0, last: null })).toBe(false)
    expect(findReady({ runtimeMs: FIND_RUNTIME_MS, tokens: FIND_TOKEN_GOAL, found: 0, last: null })).toBe(true)
  })

  it('reach every rarity and keep legendary rarest', () => {
    const counts = Object.fromEntries(rarities.map(rarity => [rarity, 0]))
    let seed = 12345
    const random = () => { seed = (seed * 1103515245 + 12345) % 2147483648; return seed / 2147483648 }
    for (let index = 0; index < 20000; index++) counts[itemOf(rollFind(random)).rarity]++
    for (const rarity of rarities) expect(counts[rarity]).toBeGreaterThan(0)
    expect(counts.legendary).toBeLessThan(counts.epic)
    expect(counts.epic).toBeLessThan(counts.rare)
    expect(counts.rare).toBeLessThan(counts.uncommon)
    expect(counts.uncommon).toBeLessThan(counts.common)
    expect(rollExchange('legendary', random)).toBeNull()
    for (let index = 0; index < 200; index++) expect(itemOf(rollExchange('epic', random)!).rarity).toBe('legendary')
  })
})

describe('pet exchange', () => {
  const hoard = settings({
    bag: { 'baseball-cap': 4, 'bucket-hat': 3, beanie: 2, banana: 3, 'task-board': 2, beret: 2, 'top-hat': 1 },
    outfit: { ...DEFAULT_PET_SETTINGS.outfit, head: 'top-hat' }
  })

  it('spends exactly ten spares of one rarity, never the worn copy, and yields the next rarity', () => {
    const worn = wearItem(hoard, 'baseball-cap')
    const tray = autoTray(worn.bag, worn.outfit, 'common')
    expect(tray).toHaveLength(EXCHANGE_COUNT)
    expect(tray.filter(id => id === 'baseball-cap')).toHaveLength(3)
    expect(trayValid(worn.bag, worn.outfit, tray)).toBe(true)
    expect(trayValid(worn.bag, worn.outfit, tray.slice(0, EXCHANGE_COUNT - 1))).toBe(false)
    expect(trayValid(worn.bag, worn.outfit, Array(EXCHANGE_COUNT).fill('baseball-cap'))).toBe(false)
    expect(trayValid(worn.bag, worn.outfit, Array(EXCHANGE_COUNT).fill('hard-hat'))).toBe(false)
    const before = spareTotal(worn.bag, worn.outfit, 'common')
    const after = exchangeItems(worn, tray, rollExchange('common', () => 0.2)!)
    expect(bagTotal(after.bag)).toBe(bagTotal(worn.bag) - EXCHANGE_COUNT + 1)
    expect(spareTotal(after.bag, after.outfit, 'common')).toBe(before - EXCHANGE_COUNT)
    expect(after.outfit.head).toBe('baseball-cap')
  })

  it('takes an item off when its last copy leaves the bag', () => {
    const worn = wearItem(settings({ bag: { beret: 1, 'party-hat': 10 } }), 'beret')
    const tray = Array<'party-hat'>(EXCHANGE_COUNT).fill('party-hat')
    const after = exchangeItems({ ...worn, bag: { ...worn.bag, beret: 0 } }, tray, 'wizard-hat')
    expect(after.outfit.head).toBe('none')
  })
})

describe('pet outfit', () => {
  it('follows zone rules and only wears what the bag holds', () => {
    const owned = settings({ bag: { beanie: 1, 'forehead-goggles': 1 } })
    expect(wearItem(owned, 'wizard-hat')).toBe(owned)
    const goggles = wearItem(owned, 'forehead-goggles')
    expect(wouldClear(goggles, 'beanie')).toEqual(['face'])
    const beanie = wearItem(goggles, 'beanie')
    expect(beanie.outfit).toMatchObject({ head: 'beanie', face: 'none' })
  })

  it('drops unknown or conflicting persisted items and keeps the paint while customization is off', () => {
    const raw = settings({
      palette: 4,
      bag: { beanie: 1, 'forehead-goggles': 1, 'not-an-item': 3 } as PetSettings['bag'],
      outfit: { head: 'beanie', face: 'forehead-goggles', hand: 'beanie', back: 'none', skin: 'none' } as PetSettings['outfit'],
      drops: { runtimeMs: 0, tokens: 0, found: 1, last: 'not-an-item' as never }
    })
    const clean = sanitizePetSettings(raw)
    expect(clean.bag).toEqual({ beanie: 1, 'forehead-goggles': 1 })
    expect(clean.outfit).toEqual({ head: 'beanie', face: 'none', hand: 'none', back: 'none', skin: 'none' })
    expect(clean.drops.last).toBeNull()
    expect(appearanceOf(clean)).toMatchObject({ palette: 4, head: 'beanie' })
    expect(appearanceOf({ ...clean, customization: false })).toMatchObject({ palette: -1, head: 'none' })
  })
})
