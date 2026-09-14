import { describe, expect, it } from 'vitest'
import { placeCommentEditor } from '../components/detail/viewers/browserFeedbackPlacement'

const editor = { width: 294, height: 44 }

describe('placeCommentEditor', () => {
  it('prefers the right side, 25px from the selection', () => {
    expect(placeCommentEditor({ x: 100, y: 100, width: 200, height: 50 }, { width: 1000, height: 600 }, editor))
      .toEqual({ left: 325, top: 100 })
  })

  it('falls back to the left, then below, then above', () => {
    expect(placeCommentEditor({ x: 600, y: 100, width: 200, height: 50 }, { width: 1000, height: 600 }, editor))
      .toEqual({ left: 281, top: 100 })
    expect(placeCommentEditor({ x: 40, y: 100, width: 320, height: 50 }, { width: 400, height: 600 }, editor))
      .toEqual({ left: 40, top: 175 })
    expect(placeCommentEditor({ x: 40, y: 200, width: 320, height: 60 }, { width: 400, height: 300 }, editor))
      .toEqual({ left: 40, top: 131 })
  })

  it('clamps to the page margin when nothing fits', () => {
    expect(placeCommentEditor({ x: 0, y: 0, width: 300, height: 100 }, { width: 300, height: 100 }, editor))
      .toEqual({ left: 16, top: 16 })
  })
})
