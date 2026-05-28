import { describe, it, expect } from 'vitest'
import {
  formatCronCollapsedLabel,
  formatCronRunningLabel,
  formatCronResultLines
} from '../utils/cronToolDisplay'

const en = 'en' as const

describe('formatCronCollapsedLabel', () => {
  it('summarizes add with everySeconds', () => {
    expect(
      formatCronCollapsedLabel(
        {
          action: 'add',
          message: 'hello',
          everySeconds: 3600
        },
        en
      )
    ).toBe('Schedule "hello" every 1h')
  })

  it('summarizes add with delaySeconds', () => {
    expect(
      formatCronCollapsedLabel(
        {
          action: 'add',
          message: 'x',
          delaySeconds: 120
        },
        en
      )
    ).toBe('Schedule "x" in 2m')
  })

  it('summarizes add with everySeconds and delaySeconds', () => {
    expect(
      formatCronCollapsedLabel(
        {
          action: 'add',
          message: 'x',
          everySeconds: 3600,
          delaySeconds: 600
        },
        en
      )
    ).toBe('Schedule "x" in 10m, then every 1h')
  })

  it('summarizes daily with dailyTime', () => {
    expect(
      formatCronCollapsedLabel(
        {
          action: 'add',
          message: 'tea',
          dailyTime: '15:00',
          timeZone: 'Asia/Shanghai'
        },
        en
      )
    ).toBe('Schedule "tea" daily at 15:00 (Asia/Shanghai)')
  })

  it('truncates long message when name absent', () => {
    const long = 'a'.repeat(50)
    const out = formatCronCollapsedLabel({ action: 'add', message: long }, en)
    expect(out.startsWith('Schedule "')).toBe(true)
    expect(out).toContain('…')
  })

  it('uses list and remove actions', () => {
    expect(formatCronCollapsedLabel({ action: 'list' }, en)).toBe('List scheduled jobs')
    expect(formatCronCollapsedLabel({ action: 'remove', jobId: 'abc' }, en)).toBe('Remove job abc')
  })
})

describe('formatCronRunningLabel', () => {
  it('returns action-specific running text', () => {
    expect(formatCronRunningLabel({ action: 'list' }, en)).toBe('Listing scheduled jobs…')
    expect(formatCronRunningLabel({ action: 'add' }, en)).toBe('Scheduling…')
    expect(formatCronRunningLabel({ action: 'remove', jobId: 'z' }, en)).toBe('Removing job z…')
  })
})

describe('formatCronResultLines', () => {
  it('parses error field', () => {
    expect(formatCronResultLines(JSON.stringify({ error: 'bad' }), en)).toEqual(['Error: bad'])
  })

  it('parses created with camelCase keys', () => {
    const next = Date.now()
    const json = JSON.stringify({
      status: 'created',
      id: 'j1',
      name: 'My job',
      nextRun: next
    })
    const lines = formatCronResultLines(json, en)
    expect(lines).toHaveLength(1)
    expect(lines![0]).toContain('Created: My job')
    expect(lines![0]).toContain('triggers at')
  })

  it('parses created with PascalCase keys', () => {
    const next = Date.now()
    const json = JSON.stringify({
      status: 'created',
      Id: 'j1',
      Name: 'N',
      nextRun: next
    })
    const lines = formatCronResultLines(json, en)
    expect(lines![0]).toContain('Created: N')
  })

  it('parses removed and not_found', () => {
    expect(formatCronResultLines(JSON.stringify({ status: 'removed', jobId: 'a' }), en)).toEqual([
      'Removed job a'
    ])
    expect(formatCronResultLines(JSON.stringify({ status: 'not_found', jobId: 'b' }), en)).toEqual([
      'Job b not found'
    ])
  })

  it('parses list count', () => {
    expect(formatCronResultLines(JSON.stringify({ count: 0 }), en)).toEqual(['No scheduled jobs'])
    expect(formatCronResultLines(JSON.stringify({ count: 2 }), en)).toEqual(['2 scheduled jobs'])
    expect(formatCronResultLines(JSON.stringify({ count: 1 }), en)).toEqual(['1 scheduled job'])
  })

  it('returns null for invalid JSON or unknown shape', () => {
    expect(formatCronResultLines('not json', en)).toBe(null)
    expect(formatCronResultLines(JSON.stringify({ foo: 1 }), en)).toBe(null)
    expect(formatCronResultLines(undefined, en)).toBe(null)
    expect(formatCronResultLines('', en)).toBe(null)
  })
})
