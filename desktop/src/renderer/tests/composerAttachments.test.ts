import { describe, expect, it, vi } from 'vitest'
import {
  classifyDroppedComposerFiles,
  mergeComposerFileAttachments
} from '../utils/composerAttachments'

function dropped(...files: File[]) {
  return {
    files: files as unknown as FileList,
    items: files.map((file) => ({
      kind: 'file',
      getAsFile: () => file,
      webkitGetAsEntry: () => ({ isDirectory: false })
    })) as unknown as DataTransferItemList
  }
}

describe('composerAttachments', () => {
  it('attaches resolved external files, skips unresolved ones and keeps images without resolving paths', () => {
    const note = new File(['notes'], 'notes.txt', { type: 'text/plain' })
    const image = new File(['png'], 'image.png', { type: 'image/png' })
    const resolvePath = vi.fn(() => 'C:\\temp\\notes.txt')

    expect(classifyDroppedComposerFiles(dropped(note, image), resolvePath)).toEqual({
      imageFiles: [image],
      fileAttachments: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }],
      skippedCount: 0
    })
    expect(resolvePath).toHaveBeenCalledExactlyOnceWith(note)

    expect(classifyDroppedComposerFiles(dropped(note), () => '')).toEqual({
      imageFiles: [],
      fileAttachments: [],
      skippedCount: 1
    })
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
