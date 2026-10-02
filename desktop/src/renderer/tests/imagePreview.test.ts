import { describe, expect, it } from 'vitest'
import type { ConversationTurn } from '../types/conversation'
import { collectConversationImages, galleryForImage, userImageKeys } from '../components/conversation/imagePreview/galleryImages'
import {
  movedBeyondClickSlop,
  pinchImageZoom,
  stepGalleryIndex,
  wheelImageZoom
} from '../components/conversation/imagePreview/imageZoom'

describe('image zoom math', () => {
  it('zooms exponentially with the wheel and stays within 10–400%', () => {
    expect(wheelImageZoom(100, -100)).toBe(272)
    expect(wheelImageZoom(100, 100)).toBe(37)
    expect(wheelImageZoom(390, -100)).toBe(400)
    expect(wheelImageZoom(12, 500)).toBe(10)
  })

  it('scales pinch zoom with finger distance', () => {
    expect(pinchImageZoom(100, 80, 150)).toBe(120)
    expect(pinchImageZoom(100, 300, 1000)).toBe(400)
  })

  it('treats a press as a backdrop click only within 5px', () => {
    expect(movedBeyondClickSlop({ x: 0, y: 0 }, { x: 3, y: 4 })).toBe(false)
    expect(movedBeyondClickSlop({ x: 0, y: 0 }, { x: 4, y: 4 })).toBe(true)
  })

  it('stops gallery navigation at both ends', () => {
    expect(stepGalleryIndex(0, -1, 3)).toBeNull()
    expect(stepGalleryIndex(1, 1, 3)).toBe(2)
    expect(stepGalleryIndex(2, 1, 3)).toBeNull()
  })
})

describe('conversation image gallery', () => {
  const turns: ConversationTurn[] = [{
    id: 'turn-1',
    threadId: 'thread-1',
    status: 'completed',
    startedAt: '2026-10-01T00:00:00.000Z',
    items: [
      {
        id: 'gen-1',
        type: 'imageGeneration',
        status: 'completed',
        imageGenerationStatus: 'completed',
        result: 'R0VO',
        mediaType: 'image/png',
        savedPath: 'C:/images/gen-1.png',
        createdAt: '2026-10-01T00:00:02.000Z'
      },
      {
        id: 'user-1',
        type: 'userMessage',
        status: 'completed',
        text: 'look',
        imageDataUrls: ['data:image/png;base64,VVNFUg=='],
        createdAt: '2026-10-01T00:00:01.000Z'
      },
      {
        id: 'remote-gen',
        type: 'imageGeneration',
        status: 'completed',
        imageGenerationStatus: 'completed',
        result: 'UkVN',
        savedPath: '/home/me/remote.png',
        savedHostId: 'ssh-host',
        createdAt: '2026-10-01T00:00:03.000Z'
      }
    ]
  }]

  it('orders user attachments before the turn output and reveals only local saved images', () => {
    const images = collectConversationImages(turns, { localFiles: true })
    expect(images.map((image) => image.key)).toEqual([
      'user-1-attachment-0-data:image/png;base64,VVNFUg==',
      'gen-1-image-0',
      'remote-gen-image-0'
    ])
    expect(images[1].revealPath).toBe('C:/images/gen-1.png')
    expect(images[2].revealPath).toBeUndefined()
    expect(collectConversationImages(turns, { localFiles: false })[1].revealPath).toBeUndefined()
  })

  it('keeps repeated attachments at their own gallery positions', () => {
    const url = 'data:image/png;base64,QQ=='
    const repeated: ConversationTurn[] = [{
      ...turns[0],
      items: [{ id: 'user-2', type: 'userMessage', text: 'again', imageDataUrls: [url, url], createdAt: '2026-10-01T00:00:00.000Z' }]
    }]
    const conversation = collectConversationImages(repeated, { localFiles: true })
    const [, second] = userImageKeys('user-2', [url, url])
    expect(galleryForImage({ key: second, src: url }, [], () => conversation).index).toBe(1)
  })

  it('opens the clicked image inside the conversation gallery, else its local group', () => {
    const conversation = collectConversationImages(turns, { localFiles: true })
    expect(galleryForImage({ key: 'gen-1-image-0' }, [], () => conversation).index).toBe(1)
    const stray = { key: 'stray', src: 'data:image/png;base64,AA==' }
    expect(galleryForImage(stray, [conversation[0], stray], () => conversation)).toEqual({
      images: [conversation[0], stray],
      index: 1
    })
  })
})
