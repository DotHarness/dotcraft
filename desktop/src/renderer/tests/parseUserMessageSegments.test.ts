import { describe, expect, it } from 'vitest'
import { parseUserMessageSegments, segmentsFromNativeInputParts } from '../components/conversation/parseUserMessageSegments'
import type { InputPart } from '../types/conversation'

describe('segmentsFromNativeInputParts commandRef rendering', () => {
  it('keeps underscore skill names intact when parsing fallback message text', () => {
    expect(parseUserMessageSegments('$browser_use ok')).toEqual([
      { type: 'skillRef', skillName: 'browser_use' },
      { type: 'text', value: ' ok' }
    ])
  })

  it('keeps underscore skill names intact from native input parts', () => {
    const parts: InputPart[] = [
      { type: 'skillRef', name: 'browser_use' }
    ]

    expect(segmentsFromNativeInputParts(parts)).toEqual([
      { type: 'skillRef', skillName: 'browser_use' }
    ])
  })

  it('splits commandRef with argsText into command chip and trailing plain text', () => {
    const parts: InputPart[] = [
      {
        type: 'commandRef',
        name: 'summarize',
        rawText: '/summarize 一些文本',
        argsText: '一些文本'
      }
    ]

    expect(segmentsFromNativeInputParts(parts)).toEqual([
      { type: 'commandRef', commandText: '/summarize' },
      { type: 'text', value: ' 一些文本' }
    ])
  })

  it('keeps command-only commandRef as a single command segment', () => {
    const parts: InputPart[] = [
      {
        type: 'commandRef',
        name: 'code-review',
        rawText: '/code-review'
      }
    ]

    expect(segmentsFromNativeInputParts(parts)).toEqual([
      { type: 'commandRef', commandText: '/code-review' }
    ])
  })

  it('falls back to parsing args from rawText when argsText is missing', () => {
    const parts: InputPart[] = [
      {
        type: 'commandRef',
        name: 'summarize',
        rawText: '/summarize some text'
      }
    ]

    expect(segmentsFromNativeInputParts(parts)).toEqual([
      { type: 'commandRef', commandText: '/summarize' },
      { type: 'text', value: ' some text' }
    ])
  })

  it('keeps fileRef displayPath separate from the real target path', () => {
    const parts: InputPart[] = [
      {
        type: 'fileRef',
        path: 'C:\\temp\\notes.txt',
        displayPath: 'notes.txt'
      }
    ]

    expect(segmentsFromNativeInputParts(parts)).toEqual([
      { type: 'fileRef', relativePath: 'notes.txt', targetPath: 'C:\\temp\\notes.txt' }
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

  it('accepts absolute paths and file URLs', () => {
    expect(parseUserMessageSegments('[a](C:/repo/a.ts:4-9) [b](file:///home/me/b.md)')).toEqual([
      { type: 'fileRef', relativePath: 'C:/repo/a.ts', targetPath: 'C:/repo/a.ts:4-9' },
      { type: 'text', value: ' ' },
      { type: 'fileRef', relativePath: '/home/me/b.md', targetPath: 'file:///home/me/b.md' }
    ])
  })

  it('accepts line fragments', () => {
    expect(parseUserMessageSegments('[notes](docs/notes.md#L10)')).toEqual([
      { type: 'fileRef', relativePath: 'docs/notes.md', targetPath: 'docs/notes.md#L10' }
    ])
  })

  it('uses the link target rather than the label', () => {
    expect(parseUserMessageSegments('[other.ts](src/real.ts)')).toEqual([
      { type: 'fileRef', relativePath: 'src/real.ts', targetPath: 'src/real.ts' }
    ])
  })

  it('keeps web links, images and bare paths literal', () => {
    for (const text of ['[docs](https://example.com/a.md)', '[mail](mailto:me@example.com)', '![shot](shot.png)', 'notes.md:2']) {
      expect(parseUserMessageSegments(text)).toEqual([{ type: 'text', value: text }])
    }
  })

  it('recognizes file links inside native text parts', () => {
    expect(segmentsFromNativeInputParts([{ type: 'text', text: 'Fix [x](a.ts:3)' }])).toEqual([
      { type: 'text', value: 'Fix ' },
      { type: 'fileRef', relativePath: 'a.ts', targetPath: 'a.ts:3' }
    ])
  })
})
