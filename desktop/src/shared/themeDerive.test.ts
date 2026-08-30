import { describe, expect, it } from 'vitest'

import { deriveThemeProperties, normalizeContrast, textOnAccent } from './themeDerive'
import { DEFAULT_SEEDS } from './themeSeed'

describe('normalizeContrast', () => {
  it('lands on baseline/100 at each variant default', () => {
    // The percentages authored in tokens.css are solved against these two numbers, so a
    // change here silently reweights every ramp.
    expect(normalizeContrast(DEFAULT_SEEDS.dark.contrast, 'dark')).toBeCloseTo(0.6, 10)
    expect(normalizeContrast(DEFAULT_SEEDS.light.contrast, 'light')).toBeCloseTo(0.45, 10)
  })

  it('rises monotonically and steepens above the baseline', () => {
    const below = normalizeContrast(45, 'dark') - normalizeContrast(30, 'dark')
    const above = normalizeContrast(90, 'dark') - normalizeContrast(75, 'dark')
    expect(below).toBeGreaterThan(0)
    expect(above).toBeCloseTo(below * 2, 10)
  })

  it('stays readable at the floor', () => {
    // --text-secondary is 60% + k * 10%; the floor must not collapse it toward the surface.
    expect(60 + normalizeContrast(0, 'dark') * 10).toBeGreaterThan(50)
    expect(65.5 + normalizeContrast(0, 'light') * 10).toBeGreaterThan(50)
  })
})

describe('textOnAccent', () => {
  it('picks white on both default accents', () => {
    expect(textOnAccent(DEFAULT_SEEDS.dark.accent)).toBe('#ffffff')
    expect(textOnAccent(DEFAULT_SEEDS.light.accent)).toBe('#ffffff')
  })

  it('picks black on a light accent', () => {
    expect(textOnAccent('#eab308')).toBe('#000000')
    expect(textOnAccent('#ffffff')).toBe('#000000')
  })

  it('picks the legible foreground on a bright blue, not the conventional white', () => {
    // #339cff carries white at 2.89:1 and black at 7.27:1.
    expect(textOnAccent('#339cff')).toBe('#000000')
  })
})

describe('deriveThemeProperties', () => {
  it('writes nothing for the default seed, so the stylesheet stays the answer', () => {
    for (const variant of ['dark', 'light'] as const) {
      const properties = deriveThemeProperties(DEFAULT_SEEDS[variant], variant)
      expect(Object.values(properties).every((value) => value === null)).toBe(true)
    }
  })

  it('treats an absent or undefined field as the default', () => {
    expect(deriveThemeProperties(null, 'dark')['--seed-accent']).toBeNull()
    expect(deriveThemeProperties({ accent: undefined }, 'dark')['--seed-accent']).toBeNull()
  })

  it('writes only the fields a custom seed moved', () => {
    const properties = deriveThemeProperties({ accent: '#eab308' }, 'dark')
    expect(properties['--seed-accent']).toBe('#eab308')
    expect(properties['--on-accent']).toBe('#000000')
    expect(properties['--seed-surface']).toBeNull()
    expect(properties['--seed-ink']).toBeNull()
    expect(properties['--contrast-k']).toBeNull()
  })

  it('writes the normalized multiplier, not the raw contrast', () => {
    expect(deriveThemeProperties({ contrast: 100 }, 'dark')['--contrast-k'])
      .toBe(String(normalizeContrast(100, 'dark')))
  })
})
