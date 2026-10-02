import { describe, expect, it } from 'vitest'

import {
  conversationAsideLayout,
  conversationAsideTier
} from '../components/conversation/conversationAside/conversationAsideLayout'

describe('conversation aside layout', () => {
  it('picks the tier from the side space', () => {
    expect(conversationAsideTier(179.5)).toBe('overlay')
    expect(conversationAsideTier(180)).toBe('shift')
    expect(conversationAsideTier(399.5)).toBe('shift')
    expect(conversationAsideTier(400)).toBe('gutter')
  })

  it('moves the column toward the leading edge only for a trailing pin in the shift tier', () => {
    expect(conversationAsideLayout(1168, 768, true, false)).toMatchObject({
      tier: 'shift',
      shift: 153,
      trailingWidth: 353,
      leadingWidth: 47
    })
    expect(conversationAsideLayout(1168, 768, false, false).shift).toBe(0)
    expect(conversationAsideLayout(1568, 768, true, false).shift).toBe(0)
    expect(conversationAsideLayout(968, 768, true, false).shift).toBe(0)
  })

  it('starts the leading seat after the turn navigation rail', () => {
    const layout = conversationAsideLayout(1668, 768, false, true)
    expect(layout.leadingStart).toBe(52)
    expect(layout.leadingWidth).toBe(398)
    expect(conversationAsideLayout(800, 768, false, true).leadingWidth).toBe(0)
  })
})
