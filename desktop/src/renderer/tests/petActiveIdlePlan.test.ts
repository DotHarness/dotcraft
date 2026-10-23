import { describe, expect, it } from 'vitest'
import { petActiveIdlePlan } from '../components/desktopPet/petActiveIdlePlan'

const viewport = { width: 1920, height: 1080 }

describe('petActiveIdlePlan', () => {
  it('picks a direction with room, grounds trips near the top edge, and skips narrow displays', () => {
    expect(petActiveIdlePlan({ x: 600, y: 300, size: 59 }, viewport)).toEqual({ direction: 'left', motions: ['hop', 'rocket', 'hover'] })
    expect(petActiveIdlePlan({ x: 20, y: 300, size: 59 }, viewport)).toEqual({ direction: 'right', motions: ['hop', 'rocket', 'hover'] })
    expect(petActiveIdlePlan({ x: 600, y: 50, size: 59 }, viewport)?.motions).toEqual(['hop'])
    expect(petActiveIdlePlan({ x: 600, y: 100, size: 59 }, viewport)?.motions).toEqual(['hop', 'rocket'])
    expect(petActiveIdlePlan({ x: 200, y: 300, size: 59 }, { width: 500, height: 800 })).toBeNull()
  })
})
