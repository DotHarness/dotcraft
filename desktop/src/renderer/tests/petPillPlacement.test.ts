import { describe, expect, it } from 'vitest'
import { placePetPill } from '../components/desktopPet/petPillPlacement'

const viewport = { width: 1024, height: 768 }

describe('placePetPill', () => {
  it('centres the pill under the pet and clamps it to the screen margins', () => {
    expect(placePetPill({ position: { x: 200, y: 200, size: 59 }, viewport, height: 100 })).toMatchObject({ left: 72, top: 271, width: 315, anchor: 'below' })
    expect(placePetPill({ position: { x: 4, y: 200, size: 59 }, viewport, height: 100 }).left).toBe(12)
    expect(placePetPill({ position: { x: 990, y: 200, size: 59 }, viewport, height: 100 }).left).toBe(697)
  })
  it('flips above the pet when the room below runs out, and back only with spare room', () => {
    const low = { x: 300, y: 700, size: 59 }
    const above = placePetPill({ position: low, viewport, height: 120 })
    expect(above).toMatchObject({ anchor: 'above', top: 700 - 12 - 120 })
    expect(placePetPill({ position: { ...low, y: 630 }, viewport, height: 120, previous: 'above' }).anchor).toBe('above')
    expect(placePetPill({ position: { ...low, y: 540 }, viewport, height: 120, previous: 'above' }).anchor).toBe('below')
  })
  it('bounds the pill by the distance to the screen edge and shrinks on narrow displays', () => {
    expect(placePetPill({ position: { x: 100, y: 100, size: 59 }, viewport, height: 50 }).maxHeight).toBe(768 - 159 - 24)
    expect(placePetPill({ position: { x: 10, y: 100, size: 59 }, viewport: { width: 200, height: 400 }, height: 50 }).width).toBe(176)
  })
})
