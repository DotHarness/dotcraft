import { describe, expect, it } from 'vitest'
import { userSegments } from './userSegments'

const section = [
  '# Diff comments:',
  '',
  '## User Comment 1',
  'File: src/app.ts',
  'Side: R',
  'Lines: 12-14',
  'Comment:',
  'Rename this',
  '',
  '## User Comment 2',
  'Artifact path: /workspace/model.xlsx',
  'Artifact type: workbook',
  'Annotation target: Revenue!B6:D8',
  'Artifact annotation: range Revenue!B6:D8 with 3 rows x 3 columns',
  'Comment:',
  'Check totals',
].join('\n')

describe('sent message comments', () => {
  it('shows comments as file references instead of the raw comment section', () => {
    const expected = [
      { type: 'text', value: 'Please fix' },
      { type: 'text', value: '\n' },
      { type: 'file', path: 'src/app.ts', display: 'src/app.ts:12-14' },
      { type: 'text', value: ' Rename this' },
      { type: 'text', value: '\n' },
      { type: 'file', path: '/workspace/model.xlsx', display: '/workspace/model.xlsx · Revenue!B6:D8' },
      { type: 'text', value: ' Check totals' },
    ]
    expect(userSegments('ignored', [{ type: 'text', text: 'Please fix' }, { type: 'text', text: `\n\n${section}` }])).toEqual(expected)
    expect(userSegments(`Please fix\n\n${section}`, null)).toEqual(expected)
  })

  it('leaves text without a comment section as typed', () => {
    expect(userSegments('# Diff comments: none', null)).toEqual([{ type: 'text', value: '# Diff comments: none' }])
  })
})
