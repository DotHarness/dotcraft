import { describe, expect, it } from 'vitest'
import { parseUserMessageSegments, segmentsFromNativeInputParts } from '../components/conversation/parseUserMessageSegments'

describe('segmentsFromNativeInputParts', () => {
  it('keeps underscore skill names intact from text and native parts', () => {
    expect(parseUserMessageSegments('$browser_use ok')).toEqual([
      { type: 'skillRef', skillName: 'browser_use' },
      { type: 'text', value: ' ok' }
    ])
    expect(segmentsFromNativeInputParts([{ type: 'skillRef', name: 'browser_use' }])).toEqual([
      { type: 'skillRef', skillName: 'browser_use' }
    ])
  })

  it('splits commandRef into a command chip and trailing args from argsText or rawText', () => {
    expect(segmentsFromNativeInputParts([
      { type: 'commandRef', name: 'summarize', rawText: '/summarize 一些文本', argsText: '一些文本' }
    ])).toEqual([
      { type: 'commandRef', commandText: '/summarize' },
      { type: 'text', value: ' 一些文本' }
    ])
    expect(segmentsFromNativeInputParts([
      { type: 'commandRef', name: 'summarize', rawText: '/summarize some text' }
    ])).toEqual([
      { type: 'commandRef', commandText: '/summarize' },
      { type: 'text', value: ' some text' }
    ])
    expect(segmentsFromNativeInputParts([
      { type: 'commandRef', name: 'code-review', rawText: '/code-review' }
    ])).toEqual([{ type: 'commandRef', commandText: '/code-review' }])
  })

  it('keeps fileRef displayPath separate from the real target path', () => {
    expect(segmentsFromNativeInputParts([
      { type: 'fileRef', path: 'C:\\temp\\notes.txt', displayPath: 'notes.txt' }
    ])).toEqual([
      { type: 'fileRef', relativePath: 'notes.txt', targetPath: 'C:\\temp\\notes.txt' }
    ])
  })

  it('recognizes file links inside native text parts', () => {
    expect(segmentsFromNativeInputParts([{ type: 'text', text: 'Fix [x](a.ts:3)' }])).toEqual([
      { type: 'text', value: 'Fix ' },
      { type: 'fileRef', relativePath: 'a.ts', targetPath: 'a.ts:3' }
    ])
  })
})

describe('parseUserMessageSegments Markdown file links', () => {
  it('turns a relative file link into a file reference that keeps its position', () => {
    expect(parseUserMessageSegments('See [the handler](src/app.ts:12:3) now')).toEqual([
      { type: 'text', value: 'See ' },
      { type: 'fileRef', relativePath: 'src/app.ts', targetPath: 'src/app.ts:12:3' },
      { type: 'text', value: ' now' }
    ])
  })

  it('accepts absolute paths, file URLs and line fragments, using the target rather than the label', () => {
    expect(parseUserMessageSegments('[a](C:/repo/a.ts:4-9) [b](file:///home/me/b.md)')).toEqual([
      { type: 'fileRef', relativePath: 'C:/repo/a.ts', targetPath: 'C:/repo/a.ts:4-9' },
      { type: 'text', value: ' ' },
      { type: 'fileRef', relativePath: '/home/me/b.md', targetPath: 'file:///home/me/b.md' }
    ])
    expect(parseUserMessageSegments('[other.ts](docs/notes.md#L10)')).toEqual([
      { type: 'fileRef', relativePath: 'docs/notes.md', targetPath: 'docs/notes.md#L10' }
    ])
  })

  it('keeps web links, images and bare paths literal', () => {
    for (const text of ['[docs](https://example.com/a.md)', '[mail](mailto:me@example.com)', '![shot](shot.png)', 'notes.md:2']) {
      expect(parseUserMessageSegments(text)).toEqual([{ type: 'text', value: text }])
    }
  })
})
