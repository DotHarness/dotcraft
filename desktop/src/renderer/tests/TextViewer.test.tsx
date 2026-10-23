import { describe, expect, it } from 'vitest'
import { navigationRowIndex } from '../components/detail/viewers/useNavigationLine'

describe('navigationRowIndex', () => {
  it('converts a one-based hint line to a clamped row index and ignores unusable hints', () => {
    expect(navigationRowIndex({ line: 2 }, 3)).toBe(1)
    expect(navigationRowIndex({ line: 200 }, 2)).toBe(1)
    expect(navigationRowIndex({ line: 0 }, 3)).toBeUndefined()
    expect(navigationRowIndex(undefined, 3)).toBeUndefined()
    expect(navigationRowIndex({ line: 1 }, 0)).toBeUndefined()
  })
})
