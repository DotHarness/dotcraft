import { beforeEach, describe, expect, it, vi } from 'vitest'

const writeImage = vi.hoisted(() => vi.fn())

vi.mock('electron', () => ({ clipboard: { writeImage } }))

import {
  copyInlineVisualizationImage,
  normalizeCaptureRect
} from '../inlineVisualizationCapture'

describe('inline visualization image capture', () => {
  beforeEach(() => vi.clearAllMocks())

  it('normalizes the trusted renderer rectangle', () => {
    expect(normalizeCaptureRect({ x: 1.4, y: 2.6, width: 735.6, height: 361.5 })).toEqual({
      x: 1,
      y: 3,
      width: 736,
      height: 362
    })
  })

  it.each([
    { x: -1, y: 0, width: 100, height: 100 },
    { x: 0, y: 0, width: 0, height: 100 },
    { x: 0, y: 0, width: 4097, height: 100 },
    { x: 0, y: 0, width: 4096, height: 10_000 }
  ])('rejects unsafe or oversized rectangles: $width x $height', rect => {
    expect(() => normalizeCaptureRect(rect)).toThrow()
  })

  it('captures, normalizes, and copies the image without returning pixels to the renderer', async () => {
    const copied = { isEmpty: () => false }
    const captured = {
      isEmpty: () => false,
      resize: vi.fn(() => copied)
    }
    const webContents = {
      capturePage: vi.fn(async () => captured)
    }

    await expect(copyInlineVisualizationImage(webContents as never, { x: 12, y: 18, width: 736, height: 362 }))
      .resolves.toEqual({ width: 736, height: 362 })
    expect(webContents.capturePage).toHaveBeenCalledWith(
      { x: 12, y: 18, width: 736, height: 362 },
      { stayHidden: true }
    )
    expect(captured.resize).toHaveBeenCalledWith({ width: 736, height: 362, quality: 'best' })
    expect(writeImage).toHaveBeenCalledWith(copied)
  })

  it('does not touch the clipboard when capture is empty', async () => {
    const webContents = {
      capturePage: vi.fn(async () => ({ isEmpty: () => true }))
    }

    await expect(copyInlineVisualizationImage(webContents as never, { x: 0, y: 0, width: 100, height: 100 }))
      .rejects.toThrow('empty')
    expect(writeImage).not.toHaveBeenCalled()
  })
})
