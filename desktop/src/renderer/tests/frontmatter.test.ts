import { describe, expect, it } from 'vitest'
import { parseFrontmatter } from '../utils/frontmatter'

describe('parseFrontmatter', () => {
  it('reads scalars, lists, nested keys and block text', () => {
    const text = [
      '---',
      'name: commit-message-style',
      'description: "Single-line subjects: no body"',
      'tags: ["git, history", style]',
      'metadata:',
      '  type: feedback',
      '  owners:',
      '    - docs',
      '    - desktop',
      'notes: |',
      '  first',
      '  second',
      '---',
      '',
      'Body'
    ].join('\n')

    expect(parseFrontmatter(text)).toEqual({
      entries: [
        { key: 'name', value: 'commit-message-style' },
        { key: 'description', value: 'Single-line subjects: no body' },
        { key: 'tags', value: ['git, history', 'style'] },
        { key: 'metadata.type', value: 'feedback' },
        { key: 'metadata.owners', value: ['docs', 'desktop'] },
        { key: 'notes', value: 'first\nsecond' }
      ],
      end: text.indexOf('\nBody')
    })
  })

  it('ignores a document without a closed leading fence', () => {
    expect(parseFrontmatter('# Title\n---\nname: x\n---')).toBeNull()
    expect(parseFrontmatter('---\nname: x\n')).toBeNull()
  })
})
