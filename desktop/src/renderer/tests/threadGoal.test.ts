import { describe, it, expect } from 'vitest'
import { buildGoalObjective } from '../utils/threadGoal'

describe('buildGoalObjective', () => {
  it('returns trimmed plain text when there are no attachments', () => {
    expect(buildGoalObjective({ text: '  Improve test coverage  ' })).toBe('Improve test coverage')
  })

  it('returns an empty string when nothing is provided', () => {
    expect(buildGoalObjective({ text: '' })).toBe('')
  })

  it('serializes inline file refs from segments in place', () => {
    const objective = buildGoalObjective({
      text: '',
      segments: [
        { type: 'text', value: 'Refactor ' },
        { type: 'file', relativePath: 'src/app.ts' },
        { type: 'text', value: ' carefully' }
      ]
    })
    expect(objective).toBe('Refactor @src/app.ts carefully')
  })

  it('appends attached files as a labeled Referenced files section', () => {
    const objective = buildGoalObjective({
      text: 'Audit the parser',
      files: [
        { path: 'C:\\repo\\parser.ts', fileName: 'parser.ts' },
        { path: 'C:\\repo\\lexer.ts', fileName: 'lexer.ts' }
      ]
    })
    expect(objective).toBe(
      'Audit the parser\n\nReferenced files:\n- [File #1]: C:\\repo\\parser.ts\n- [File #2]: C:\\repo\\lexer.ts'
    )
  })

  it('appends images by temp path under Referenced image files', () => {
    const objective = buildGoalObjective({
      text: 'Match this mockup',
      images: [
        { tempPath: 'C:\\tmp\\image-1.png', dataUrl: 'data:...', fileName: 'image-1.png', mimeType: 'image/png' }
      ]
    })
    expect(objective).toBe(
      'Match this mockup\n\nReferenced image files:\n- [Image #1]: C:\\tmp\\image-1.png'
    )
  })

  it('combines text, files and images with blank-line separators', () => {
    const objective = buildGoalObjective({
      text: 'Goal body',
      files: [{ path: '/a/notes.md', fileName: 'notes.md' }],
      images: [{ tempPath: '/a/shot.png', dataUrl: 'd', fileName: 'shot.png', mimeType: 'image/png' }]
    })
    expect(objective).toBe(
      'Goal body\n\nReferenced files:\n- [File #1]: /a/notes.md\n\nReferenced image files:\n- [Image #1]: /a/shot.png'
    )
  })
})
