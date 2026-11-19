import { describe, expect, it } from 'vitest'
import { parseMemoryFile, serializeMemoryFile } from './memoryFile'

describe('memory file headers', () => {
  it('keeps header fields it does not own when only the body changes', () => {
    const source = [
      '---',
      'name: test-style',
      'description: >-',
      '  Prefers behavior tests',
      '  over snapshots',
      'metadata: { type: feedback, source: imported }',
      'origin: 2026-11-19',
      '---',
      '',
      'Old body.'
    ].join('\n')
    const parsed = parseMemoryFile(source)
    const saved = parseMemoryFile(serializeMemoryFile({ ...parsed, body: 'New body.' }, parsed.extra))

    expect(saved).toMatchObject({
      name: 'test-style',
      description: 'Prefers behavior tests over snapshots',
      type: 'feedback',
      body: 'New body.\n',
      extra: { top: { origin: '2026-11-19' }, metadata: { source: 'imported' } }
    })
  })

  it('marks a header that is not a YAML mapping', () => {
    expect(parseMemoryFile('---\nname: [unclosed\n---\nBody').invalidHeader).toBe(true)
  })
})
