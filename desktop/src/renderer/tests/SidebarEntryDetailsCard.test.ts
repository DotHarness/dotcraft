import { describe, expect, it } from 'vitest'
import { placeSidebarEntryDetailsCard } from '../components/sidebar/SidebarEntryDetailsCard'

describe('SidebarEntryDetailsCard placement', () => {
  it('opens right with an eight-pixel overlap and aligned top edge', () => {
    const placement = placeSidebarEntryDetailsCard(
      { left: 0, right: 240, top: 40, width: 240, height: 28 },
      { width: 320, height: 104 },
      1200,
      800
    )

    expect(placement).toEqual({
      left: 232,
      top: 40,
      side: 'right',
      overlapEdge: 'left'
    })
  })

  it('flips left and mirrors the eight-pixel overlap edge', () => {
    const placement = placeSidebarEntryDetailsCard(
      { left: 960, right: 1200, top: 64, width: 240, height: 28 },
      { width: 240, height: 96 },
      1200,
      800
    )

    expect(placement).toEqual({
      left: 728,
      top: 64,
      side: 'left',
      overlapEdge: 'right'
    })
  })

  it('clamps the card to the top and bottom viewport padding', () => {
    expect(placeSidebarEntryDetailsCard(
      { left: 0, right: 240, top: 2, width: 240, height: 28 },
      { width: 240, height: 100 },
      1000,
      800
    ).top).toBe(8)

    expect(placeSidebarEntryDetailsCard(
      { left: 0, right: 240, top: 760, width: 240, height: 28 },
      { width: 240, height: 100 },
      1000,
      800
    ).top).toBe(692)
  })

  it('omits the overlap edge when horizontal clamping breaks attachment', () => {
    const placement = placeSidebarEntryDetailsCard(
      { left: 130, right: 170, top: 40, width: 40, height: 28 },
      { width: 280, height: 100 },
      300,
      800
    )

    expect(placement.left).toBe(8)
    expect(placement.side).toBe('left')
    expect(placement.overlapEdge).toBeNull()
  })
})
