import { describe, expect, it } from 'vitest'
import { clampPet, inPetDetachZone } from './desktopPet'

describe('pet screen placement', () => {
  it('detects all four window edges without requiring release', () => {
    const viewport = { width: 1400, height: 800 }
    for (const point of [{ x: 48, y: 400 }, { x: 1352, y: 400 }, { x: 700, y: 48 }, { x: 700, y: 752 }]) {
      expect(inPetDetachZone(point, viewport)).toBe(true)
    }
    expect(inPetDetachZone({ x: 700, y: 400 }, viewport)).toBe(false)
  })
  const area = { x: -1920, y: -200, width: 1920, height: 1040 }
  it('keeps the companion within negative-origin display work areas', () => {
    expect(clampPet({ x: -3000, y: -400 }, area)).toEqual({ x: -1912, y: -192 })
    expect(clampPet({ x: 500, y: 1500 }, area)).toEqual({ x: -88, y: 752 })
  })
  it('snaps only near a side edge on release', () => {
    expect(clampPet({ x: -1900, y: 200 }, area, 80, false).x).toBe(-1900)
    expect(clampPet({ x: -1900, y: 200 }, area, 80, true).x).toBe(-1912)
    expect(clampPet({ x: -800, y: 200 }, area, 80, true).x).toBe(-800)
  })
})
