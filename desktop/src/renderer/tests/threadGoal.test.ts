import { describe, it, expect } from 'vitest'
import { buildGoalObjective } from '../utils/threadGoal'

describe('buildGoalObjective', () => {
  it('trims plain text and serializes inline file refs from segments in place', () => {
    expect(buildGoalObjective({ text: '  Improve test coverage  ' })).toBe('Improve test coverage')
    expect(buildGoalObjective({ text: '' })).toBe('')
    expect(buildGoalObjective({
      text: '',
      segments: [
        { type: 'text', value: 'Refactor ' },
        { type: 'file', relativePath: 'src/app.ts' },
        { type: 'text', value: ' carefully' }
      ]
    })).toBe('Refactor @src/app.ts carefully')
  })

  it('appends attached files and images as labeled reference sections', () => {
    expect(buildGoalObjective({
      text: 'Goal body',
      files: [
        { path: 'C:\\repo\\parser.ts', fileName: 'parser.ts' },
        { path: '/a/notes.md', fileName: 'notes.md' }
      ],
      images: [{ tempPath: '/a/shot.png', dataUrl: 'd', fileName: 'shot.png', mimeType: 'image/png' }]
    })).toBe(
      'Goal body\n\nReferenced files:\n- [File #1]: C:\\repo\\parser.ts\n- [File #2]: /a/notes.md\n\nReferenced image files:\n- [Image #1]: /a/shot.png'
    )
  })
})
