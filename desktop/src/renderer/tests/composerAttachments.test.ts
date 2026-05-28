import { describe, expect, it, vi } from 'vitest'
import {
  classifyDroppedComposerFiles,
  mergeComposerFileAttachments
} from '../utils/composerAttachments'

describe('composerAttachments', () => {
  it('keeps external dropped non-image file paths resolved by Electron as attachments', () => {
    const note = new File(['notes'], 'notes.txt', { type: 'text/plain' })
    const resolvePath = vi.fn(() => 'C:\\temp\\notes.txt')

    const result = classifyDroppedComposerFiles({
      files: [note] as unknown as FileList,
      items: [
        {
          kind: 'file',
          getAsFile: () => note,
          webkitGetAsEntry: () => ({ isDirectory: false })
        }
      ] as unknown as DataTransferItemList
    }, resolvePath)

    expect(result).toEqual({
      imageFiles: [],
      fileAttachments: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }],
      skippedCount: 0
    })
    expect(resolvePath).toHaveBeenCalledWith(note)
  })

  it('skips external non-image files when Electron cannot resolve a path', () => {
    const note = new File(['notes'], 'notes.txt', { type: 'text/plain' })

    const result = classifyDroppedComposerFiles({
      files: [note] as unknown as FileList,
      items: [
        {
          kind: 'file',
          getAsFile: () => note,
          webkitGetAsEntry: () => ({ isDirectory: false })
        }
      ] as unknown as DataTransferItemList
    }, () => '')

    expect(result).toEqual({
      imageFiles: [],
      fileAttachments: [],
      skippedCount: 1
    })
  })

  it('does not resolve local paths for dropped images', () => {
    const image = new File(['png'], 'image.png', { type: 'image/png' })
    const resolvePath = vi.fn(() => 'C:\\temp\\image.png')

    const result = classifyDroppedComposerFiles({
      files: [image] as unknown as FileList,
      items: [
        {
          kind: 'file',
          getAsFile: () => image,
          webkitGetAsEntry: () => ({ isDirectory: false })
        }
      ] as unknown as DataTransferItemList
    }, resolvePath)

    expect(result).toEqual({
      imageFiles: [image],
      fileAttachments: [],
      skippedCount: 0
    })
    expect(resolvePath).not.toHaveBeenCalled()
  })

  it('deduplicates file attachments by normalized path', () => {
    expect(
      mergeComposerFileAttachments(
        [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }],
        [{ path: 'C:\\temp\\notes.txt', fileName: 'notes-copy.txt' }]
      )
    ).toEqual([{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }])
  })
})
