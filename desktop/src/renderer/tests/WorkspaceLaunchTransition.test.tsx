import { describe, expect, it } from 'vitest'
import { centeredLaunchLogoRect } from '../components/WorkspaceLaunchTransition'

describe('WorkspaceLaunchTransition', () => {
  it('computes the centered launch rect from the viewport', () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1200 })
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 800 })

    expect(centeredLaunchLogoRect()).toEqual({
      left: 552,
      top: 352,
      width: 96,
      height: 96
    })
  })
})
