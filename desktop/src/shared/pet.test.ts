import { describe, expect, it } from 'vitest'
import { DEFAULT_PET_SETTINGS, normalizePetSetting, resolvePetSettings } from './pet'

describe('resolvePetSettings', () => {
  it('returns the defaults for missing or corrupt input', () => {
    expect(resolvePetSettings(undefined)).toEqual(DEFAULT_PET_SETTINGS)
    expect(resolvePetSettings('nope')).toEqual(DEFAULT_PET_SETTINGS)
    expect(resolvePetSettings({ customization: 'yes', palette: 'blue', outfit: 4, bag: [], drops: null })).toEqual(DEFAULT_PET_SETTINGS)
  })

  it('clamps the palette and keeps only well-formed outfit, bag, and progress fields', () => {
    const settings = resolvePetSettings({
      customization: false,
      palette: 99.6,
      outfit: { head: 'beret', hand: 'Coffee Mug', back: 'x'.repeat(41), skin: 42, extra: 'poop' },
      bag: { beret: 2, 'wizard-hat': 0, 'task-board': -3, 'bad id': 1, cape: 1e9, ufo: 1.9 },
      drops: { runtimeMs: -5, tokens: 1234.7, found: 'many', last: 'beret' }
    })
    expect(settings.customization).toBe(false)
    expect(settings.palette).toBe(11)
    expect(settings.outfit).toEqual({ head: 'beret', face: 'none', hand: 'none', back: 'none', skin: 'none' })
    expect(settings.bag).toEqual({ beret: 2, cape: 9999, ufo: 1 })
    expect(settings.drops).toEqual({ runtimeMs: 0, tokens: 1234, found: 0, last: 'beret' })
    expect(resolvePetSettings({ palette: -7 }).palette).toBe(-1)
  })

  it('persists nothing while the record equals the defaults', () => {
    expect(normalizePetSetting(undefined)).toBeUndefined()
    expect(normalizePetSetting({ customization: true, bag: {} })).toBeUndefined()
    expect(normalizePetSetting({ palette: 3 })).toMatchObject({ palette: 3 })
    expect(normalizePetSetting({ drops: { tokens: 50 } })?.drops.tokens).toBe(50)
  })
})
