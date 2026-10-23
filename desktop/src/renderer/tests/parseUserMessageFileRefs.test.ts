import { describe, expect, it } from 'vitest'
import { parseUserMessageFileRefs } from '../components/conversation/parseUserMessageFileRefs'

describe('parseUserMessageFileRefs', () => {
  it('splits @paths and $skill markers out of surrounding text', () => {
    expect(parseUserMessageFileRefs('Check @src/foo.ts then $code-review now\n@a/b @c/d')).toEqual([
      { type: 'text', value: 'Check ' },
      { type: 'fileRef', relativePath: 'src/foo.ts' },
      { type: 'text', value: ' then ' },
      { type: 'skillRef', skillName: 'code-review' },
      { type: 'text', value: ' now\n' },
      { type: 'fileRef', relativePath: 'a/b' },
      { type: 'text', value: ' ' },
      { type: 'fileRef', relativePath: 'c/d' }
    ])
  })

  it.each([
    'mail me user@domain.com',
    'x @ y',
    'Please Use Skill: memory today'
  ])('keeps %j as plain text', (text) => {
    expect(parseUserMessageFileRefs(text)).toEqual([{ type: 'text', value: text }])
  })

  it('treats attached file marker text as normal text', () => {
    expect(parseUserMessageFileRefs('[[Attached File: C:\\logs\\a.txt]]\n\nCheck @src/foo.ts')).toEqual([
      { type: 'text', value: '[[Attached File: C:\\logs\\a.txt]]\n\nCheck ' },
      { type: 'fileRef', relativePath: 'src/foo.ts' }
    ])
  })
})
