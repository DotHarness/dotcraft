import { describe, expect, it } from 'vitest'
import { parseJsonConfig, parseJsonObjectConfig, parseJsonRecordConfig } from '../../shared/jsonConfig'

describe('jsonConfig', () => {
  it('parseJsonConfig parses BOM-prefixed objects and returns the fallback for empty, non-object, or invalid JSON', () => {
    expect(parseJsonConfig('﻿{"Theme":"dark"}', {} as Record<string, unknown>)).toEqual({ Theme: 'dark' })
    const fallback = { Theme: 'system' }
    for (const content of ['   ', '["a", "b"]', '"value"', '{invalid-json']) {
      expect(parseJsonConfig(content, fallback)).toBe(fallback)
    }
  })

  it('parseJsonObjectConfig parses BOM-prefixed objects, treats empty input as {}, and throws otherwise', () => {
    expect(parseJsonObjectConfig('﻿{"Theme":"dark"}')).toEqual({ Theme: 'dark' })
    expect(parseJsonObjectConfig('   ')).toEqual({})
    expect(() => parseJsonObjectConfig('["a", "b"]')).toThrow('Config payload must be a JSON object')
    expect(() => parseJsonObjectConfig('"value"')).toThrow('Config payload must be a JSON object')
    expect(() => parseJsonObjectConfig('{invalid-json')).toThrow()
  })

  it('parseJsonRecordConfig parses BOM-prefixed objects, returns {} for empty or non-object JSON, and throws for invalid JSON', () => {
    expect(parseJsonRecordConfig('﻿{"Theme":"dark"}')).toEqual({ Theme: 'dark' })
    for (const content of ['   ', '["a", "b"]', '"value"', '42', 'null']) {
      expect(parseJsonRecordConfig(content)).toEqual({})
    }
    expect(() => parseJsonRecordConfig('{invalid-json')).toThrow()
  })
})
