import { describe, expect, it } from 'vitest'
import { parseUserMessageFileRefs } from '../components/conversation/parseUserMessageFileRefs'

describe('parseUserMessageFileRefs', () => {
  it('parses a single @path at start', () => {
    expect(parseUserMessageFileRefs('@src/foo.ts')).toEqual([
      { type: 'fileRef', relativePath: 'src/foo.ts' }
    ])
  })

  it('parses @path after whitespace', () => {
    expect(parseUserMessageFileRefs('See @src/foo.ts ok')).toEqual([
      { type: 'text', value: 'See ' },
      { type: 'fileRef', relativePath: 'src/foo.ts' },
      { type: 'text', value: ' ok' }
    ])
  })

  it('does not treat @ in email as file ref', () => {
    expect(parseUserMessageFileRefs('mail me user@domain.com')).toEqual([
      { type: 'text', value: 'mail me user@domain.com' }
    ])
  })

  it('parses multiple refs', () => {
    expect(parseUserMessageFileRefs('@a/b @c/d')).toEqual([
      { type: 'fileRef', relativePath: 'a/b' },
      { type: 'text', value: ' ' },
      { type: 'fileRef', relativePath: 'c/d' }
    ])
  })

  it('leaves lone @ at boundary as text', () => {
    expect(parseUserMessageFileRefs('x @ y')).toEqual([{ type: 'text', value: 'x @ y' }])
  })

  it('preserves newlines', () => {
    expect(parseUserMessageFileRefs('a\n@b/c')).toEqual([
      { type: 'text', value: 'a\n' },
      { type: 'fileRef', relativePath: 'b/c' }
    ])
  })

  it('parses $skill marker into skillRef segment', () => {
    expect(parseUserMessageFileRefs('$memory')).toEqual([
      { type: 'skillRef', skillName: 'memory' }
    ])
  })

  it('parses mixed file refs and $skill markers', () => {
    expect(parseUserMessageFileRefs('Check @src/foo.ts then $code-review now')).toEqual([
      { type: 'text', value: 'Check ' },
      { type: 'fileRef', relativePath: 'src/foo.ts' },
      { type: 'text', value: ' then ' },
      { type: 'skillRef', skillName: 'code-review' },
      { type: 'text', value: ' now' }
    ])
  })

  it('treats plain "Use Skill:" text as normal text', () => {
    expect(parseUserMessageFileRefs('Please Use Skill: memory today')).toEqual([
      { type: 'text', value: 'Please Use Skill: memory today' }
    ])
  })

  it('treats attached file marker text as normal text', () => {
    expect(parseUserMessageFileRefs('[[Attached File: C:\\logs\\a.txt]]\n\nCheck @src/foo.ts')).toEqual([
      { type: 'text', value: '[[Attached File: C:\\logs\\a.txt]]\n\nCheck ' },
      { type: 'fileRef', relativePath: 'src/foo.ts' }
    ])
  })
})
