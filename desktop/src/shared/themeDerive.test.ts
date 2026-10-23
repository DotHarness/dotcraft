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

  it('rises monotonically, steepens above the baseline, and stays readable at the floor', () => {
    const below = normalizeContrast(45, 'dark') - normalizeContrast(30, 'dark')
    const above = normalizeContrast(90, 'dark') - normalizeContrast(75, 'dark')
    expect(below).toBeGreaterThan(0)
    expect(above).toBeCloseTo(below * 2, 10)
    expect(60 + normalizeContrast(0, 'dark') * 10).toBeGreaterThan(50)
    expect(65.5 + normalizeContrast(0, 'light') * 10).toBeGreaterThan(50)
  })
})

describe('textOnAccent', () => {
  it('picks the more legible foreground', () => {
    expect(textOnAccent(DEFAULT_SEEDS.dark.accent)).toBe('#ffffff')
    expect(textOnAccent(DEFAULT_SEEDS.light.accent)).toBe('#ffffff')
    expect(textOnAccent('#eab308')).toBe('#000000')
    expect(textOnAccent('#339cff')).toBe('#000000')
  })
})

describe('deriveThemeProperties', () => {
  it('writes nothing for the default, absent, or undefined seed', () => {
    for (const variant of ['dark', 'light'] as const) {
      const properties = deriveThemeProperties(DEFAULT_SEEDS[variant], variant)
      expect(Object.values(properties).every((value) => value === null)).toBe(true)
    }
    expect(deriveThemeProperties(null, 'dark')['--seed-accent']).toBeNull()
    expect(deriveThemeProperties({ accent: undefined }, 'dark')['--seed-accent']).toBeNull()
  })

  it('writes only the fields a custom seed moved, with the normalized contrast multiplier', () => {
    const properties = deriveThemeProperties({ accent: '#eab308' }, 'dark')
    expect(properties['--seed-accent']).toBe('#eab308')
    expect(properties['--on-accent']).toBe('#000000')
    expect(properties['--seed-surface']).toBeNull()
    expect(properties['--seed-ink']).toBeNull()
    expect(properties['--contrast-k']).toBeNull()
    expect(deriveThemeProperties({ contrast: 100 }, 'dark')['--contrast-k'])
      .toBe(String(normalizeContrast(100, 'dark')))
  })
})
