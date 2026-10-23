import { describe, expect, it } from 'vitest'
import { getFallbackThreadName } from '../utils/threadFallbackName'

const fallbackNames = {
  fallbackThreadName: 'Message',
  fileFallbackThreadName: 'File reference message',
  attachmentFallbackThreadName: 'Attachment message'
} as const

describe('getFallbackThreadName', () => {
  it('prefers truncated visible text, ignoring leading structured file refs', () => {
    expect(
      getFallbackThreadName({
        visibleText: 'A'.repeat(60),
        imagesCount: 1,
        filesCount: 1,
        ...fallbackNames
      })
    ).toBe(`${'A'.repeat(50)}...`)
    expect(
      getFallbackThreadName({
        visibleText: '@C:\\temp\\notes.txt\n\nReview this change set',
        imagesCount: 0,
        filesCount: 1,
        ...fallbackNames
      })
    ).toBe('Review this change set')
  })

  it('falls back when only file refs or leaked system reminders are visible', () => {
    expect(
      getFallbackThreadName({
        visibleText: '@C:\\temp\\notes.txt\n@D:\\docs\\plan.md',
        imagesCount: 0,
        filesCount: 2,
        ...fallbackNames
      })
    ).toBe('File reference message')
    expect(
      getFallbackThreadName({
        visibleText: '<system-reminder>\n## Runtime Context\nCurrentMode: Plan\n</system-reminder>',
        imagesCount: 0,
        filesCount: 0,
        ...fallbackNames
      })
    ).toBe('Message')
  })
})
