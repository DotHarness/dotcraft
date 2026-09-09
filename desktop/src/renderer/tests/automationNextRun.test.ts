import { expect, it } from 'vitest'
import { automationNextRun } from '../utils/automationNextRun'

const now = Date.parse('2026-09-09T00:00:00Z')
it('formats future hours and days and never describes overdue runs as past activity', () => {
  expect(automationNextRun('2026-09-09T20:00:00Z', now, 'en')).toBe('Next run in 20 hours')
  expect(automationNextRun('2026-09-14T00:00:00Z', now, 'en')).toBe('Next run in 5 days')
  expect(automationNextRun('2026-09-09T00:00:01Z', now, 'en')).toBe('Next run in 1 minute')
  expect(automationNextRun('2026-09-08T00:00:00Z', now, 'en')).toBe('Due now')
  expect(automationNextRun(null, now, 'en')).toBeNull()
  expect(automationNextRun('invalid', now, 'en')).toBeNull()
})
it('localizes next-run timing in the client locale', () => {
  expect(automationNextRun('2026-09-09T20:00:00Z', now, 'zh-Hans')).toBe('下次运行：20小时后')
})
