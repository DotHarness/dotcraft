import { describe, expect, it } from 'vitest'
import type { InputPart } from '../types/conversation'
import { hasInlineEditContent, replaceInlineEditText } from '../utils/inlineMessageEdit'

describe('inline edited input', () => {
  it('replaces text without losing structured references or media', () => {
    const input: InputPart[] = [
      { type: 'fileRef', path: '/workspace/report.pdf', displayPath: 'report.pdf' },
      { type: 'text', text: 'Old ' },
      { type: 'skillRef', name: 'review' },
      { type: 'text', text: 'text' },
      { type: 'localImage', path: '/workspace/chart.png' }
    ]
    expect(replaceInlineEditText(input, 'New text', 'current')).toEqual([
      input[0], { type: 'text', text: 'New text' }, input[2], input[4]
    ])
    expect(hasInlineEditContent('', input)).toBe(true)
    expect(hasInlineEditContent(' ', [{ type: 'text', text: 'Old' }])).toBe(false)
  })

  it('rebuilds thread context from edited mentions rather than retaining deleted references', () => {
    const input: InputPart[] = [
      { type: 'contextRef', context: { id: 'old', kind: 'threadReferences', text: 'Old thread context' } },
      { type: 'text', text: 'Old text' }
    ]
    expect(hasInlineEditContent('', input)).toBe(false)
    expect(replaceInlineEditText(input, 'No references', 'current')).toEqual([{ type: 'text', text: 'No references' }])
    const changed = replaceInlineEditText(input, 'See [@Notes](thread://notes)', 'current')
    expect(changed[0]).toMatchObject({ type: 'contextRef', context: { kind: 'threadReferences', text: expect.stringContaining('"threadId":"notes"') } })
  })
})
