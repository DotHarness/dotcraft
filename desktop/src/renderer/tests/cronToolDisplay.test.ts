import { describe, it, expect } from 'vitest'
import {
  formatCronCollapsedLabel,
  formatCronResultLines
} from '../utils/cronToolDisplay'

const en = 'en' as const

describe('formatCronCollapsedLabel', () => {
  it('truncates long message when name absent', () => {
    const long = 'a'.repeat(50)
    const out = formatCronCollapsedLabel({ action: 'add', message: long }, en)
    expect(out).toContain('…')
    expect(out).not.toContain(long)
  })
})

describe('formatCronResultLines', () => {
  it('extracts created job names from camelCase and PascalCase payloads', () => {
    const next = Date.now()
    const camelCase = JSON.stringify({
      status: 'created',
      id: 'j1',
      name: 'My job',
      nextRun: next
    })
    const pascalCase = JSON.stringify({
      status: 'created',
      Id: 'j1',
      Name: 'N',
      nextRun: next
    })

    const camelLines = formatCronResultLines(camelCase, en)
    const pascalLines = formatCronResultLines(pascalCase, en)

    expect(camelLines).toHaveLength(1)
    expect(camelLines![0]).toContain('My job')
    expect(pascalLines).toHaveLength(1)
    expect(pascalLines![0]).toContain('N')
  })

  it('returns null for invalid JSON or unknown shape', () => {
    expect(formatCronResultLines('not json', en)).toBe(null)
    expect(formatCronResultLines(JSON.stringify({ foo: 1 }), en)).toBe(null)
    expect(formatCronResultLines(undefined, en)).toBe(null)
    expect(formatCronResultLines('', en)).toBe(null)
  })
})
