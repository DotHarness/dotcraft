import { describe, expect, it } from 'vitest'
import { formatOwnedNotificationPreview, isSilentReplyToken } from '../threadTasks/ownedThreadCheckIn'

describe('ownedThreadCheckIn', () => {
  it('recognizes silent reply tokens including backtick-wrapped and empty responses', () => {
    expect(isSilentReplyToken('DONT_NOTIFY', 'DONT_NOTIFY')).toBe(true)
    expect(isSilentReplyToken('  `DONT_NOTIFY`  ', 'DONT_NOTIFY')).toBe(true)
    expect(isSilentReplyToken('', 'DONT_NOTIFY')).toBe(true)
    expect(isSilentReplyToken('All tests passed.', 'DONT_NOTIFY')).toBe(false)
  })

  it('strips markdown formatting and truncates notification previews', () => {
    expect(formatOwnedNotificationPreview('**Build** passed ```ts\nok\n```')).toBe('Build passed')
    expect(formatOwnedNotificationPreview('a'.repeat(200)).length).toBe(160)
  })
})
