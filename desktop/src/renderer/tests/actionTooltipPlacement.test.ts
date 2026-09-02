import { describe, expect, it } from 'vitest'
import { placeTooltip } from '../components/ui/ActionTooltip'

function rect(left: number, top: number, width: number, height: number): DOMRect {
  return {
    left,
    top,
    width,
    height,
    right: left + width,
    bottom: top + height,
    x: left,
    y: top,
    toJSON: () => ({})
  } as DOMRect
}

const label = rect(0, 0, 80, 30)

describe('placeTooltip', () => {
  it('centres a top tooltip above the control', () => {
    const placement = placeTooltip(rect(500, 300, 24, 24), label, 'top', 1200, 800)

    expect(placement).toEqual({ left: 472, top: 262, transform: 'translateZ(0)' })
  })

  it('mirrors within the block axis when the control is against the top edge', () => {
    const placement = placeTooltip(rect(500, 12, 24, 24), label, 'top', 1200, 800)

    // Flipped below rather than clamped back on top of the control it names.
    expect(placement.top).toBe(44)
  })

  it('mirrors within the inline axis when the control is against the trailing edge', () => {
    const placement = placeTooltip(rect(1160, 300, 24, 24), label, 'right', 1200, 800)

    expect(placement.left).toBe(1072)
  })

  it('never crosses axes: a block-axis request stays on the block axis', () => {
    // No room above and none below, so the requested side survives and clamping resolves it.
    const placement = placeTooltip(rect(0, 10, 24, 24), label, 'top', 1200, 60)

    expect(placement.transform).toBe('translateZ(0)')
    expect(placement.top).toBe(8)
  })

  it('keeps the requested side when neither side of the inline axis fits', () => {
    const placement = placeTooltip(rect(60, 300, 24, 24), label, 'right', 180, 800)

    expect(placement.left).toBe(92)
    expect(placement.transform).toBeUndefined()
  })

  it('clamps the cross axis to the viewport padding', () => {
    const placement = placeTooltip(rect(2, 300, 24, 24), label, 'top', 1200, 800)

    expect(placement.left).toBe(8)
  })
})
