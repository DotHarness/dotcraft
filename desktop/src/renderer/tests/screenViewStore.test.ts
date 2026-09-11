import { describe, expect, it } from 'vitest'
import { DOCK_MARGIN, DOCK_TOP_INSET, clampDockPosition } from '../stores/screenViewStore'

const viewport = { width: 1200, height: 800 }
const size = { width: 360, height: 202 }

describe('clampDockPosition', () => {
  it('leaves a position that already fits, rounded to whole pixels', () => {
    expect(clampDockPosition({ x: 400, y: 300 }, size, viewport)).toEqual({ x: 400, y: 300 })
    expect(clampDockPosition({ x: 120.4, y: 240.6 }, size, viewport)).toEqual({ x: 120, y: 241 })
  })

  it('keeps the dock inside the window and clear of the title bar', () => {
    expect(clampDockPosition({ x: -50, y: 0 }, size, viewport)).toEqual({
      x: DOCK_MARGIN,
      y: DOCK_TOP_INSET
    })
    expect(clampDockPosition({ x: 5000, y: 5000 }, size, viewport)).toEqual({
      x: viewport.width - size.width - DOCK_MARGIN,
      y: viewport.height - size.height - DOCK_MARGIN
    })
  })

  it('pins the dock to the top-left when the window is smaller than it', () => {
    expect(clampDockPosition({ x: 400, y: 300 }, size, { width: 200, height: 120 })).toEqual({
      x: DOCK_MARGIN,
      y: DOCK_TOP_INSET
    })
  })
})
