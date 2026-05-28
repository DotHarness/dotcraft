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
