import { describe, expect, it } from 'vitest'
import { MAX_MESSAGE_PHOTO_CHARS, withinPhotoBudget } from './attachments'
import type { PhotoAttachment } from './draft'

function photo(id: string, chars: number): PhotoAttachment {
  return { id, dataUrl: 'x'.repeat(chars) }
}

const MB = 1024 * 1024

describe('photo budget', () => {
  it('refuses photos from one selection that would overflow the message', () => {
    const { accepted, refused } = withinPhotoBudget([], [photo('a', MB), photo('b', MB), photo('c', MB), photo('d', MB)])
    expect(accepted.map((item) => item.id)).toEqual(['a', 'b', 'c'])
    expect(refused).toBe(1)
  })

  it('counts photos already in the draft across later selections', () => {
    const first = withinPhotoBudget([], [photo('a', 2 * MB)])
    const second = withinPhotoBudget(first.accepted, [photo('b', 2 * MB), photo('c', MAX_MESSAGE_PHOTO_CHARS - 2 * MB)])
    expect(second.accepted.map((item) => item.id)).toEqual(['c'])
    expect(second.refused).toBe(1)
  })
})
