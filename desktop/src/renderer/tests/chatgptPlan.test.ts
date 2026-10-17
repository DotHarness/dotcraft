import { describe, expect, it } from 'vitest'
import { formatPlanName } from '../utils/chatgptPlan'

describe('Subscription plan names', () => {
  it('names known tiers', () => {
    expect(formatPlanName('plus')).toBe('Plus')
    expect(formatPlanName('prolite')).toBe('Pro 100')
    expect(formatPlanName(' PRO ')).toBe('Pro 200')
    expect(formatPlanName('promax')).toBe('Pro 500')
    expect(formatPlanName('team')).toBe('Business')
    expect(formatPlanName('business')).toBe('Enterprise')
  })

  it('capitalizes an unrecognized tier and omits a missing one', () => {
    expect(formatPlanName('starter')).toBe('Starter')
    expect(formatPlanName('unknown')).toBeNull()
    expect(formatPlanName('')).toBeNull()
    expect(formatPlanName(null)).toBeNull()
  })
})
