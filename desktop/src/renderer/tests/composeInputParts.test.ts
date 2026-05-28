import { describe, expect, it } from 'vitest'
import { buildComposerInputParts } from '../utils/composeInputParts'

describe('buildComposerInputParts', () => {
  it('emits external file attachments as structured fileRef parts', () => {
    const result = buildComposerInputParts({
      text: 'Review this file',
      files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }]
    })

    expect(result.inputParts).toEqual([
      { type: 'fileRef', path: 'C:\\temp\\notes.txt', displayPath: 'C:\\temp\\notes.txt' },
      { type: 'text', text: '\n\n' },
      { type: 'text', text: 'Review this file' }
    ])
    expect(result.visibleText).toBe('@C:\\temp\\notes.txt\n\nReview this file')
  })

  it('keeps workspace @ mentions as relative fileRef parts', () => {
    const result = buildComposerInputParts({
      text: '@src/foo.ts explain it',
      segments: [
        { type: 'file', relativePath: 'src/foo.ts' },
        { type: 'text', value: ' explain it' }
      ]
    })

    expect(result.inputParts).toEqual([
      { type: 'fileRef', path: 'src/foo.ts', displayPath: 'src/foo.ts' },
      { type: 'text', text: ' explain it' }
    ])
  })

  it('keeps multiple file attachments distinct and newline separated', () => {
    const result = buildComposerInputParts({
      text: '',
      files: [
        { path: 'C:\\temp\\a.txt', fileName: 'a.txt' },
        { path: 'D:\\docs\\b.md', fileName: 'b.md' }
      ]
    })

    expect(result.inputParts).toEqual([
      { type: 'fileRef', path: 'C:\\temp\\a.txt', displayPath: 'C:\\temp\\a.txt' },
      { type: 'text', text: '\n' },
      { type: 'fileRef', path: 'D:\\docs\\b.md', displayPath: 'D:\\docs\\b.md' }
    ])
  })
})
