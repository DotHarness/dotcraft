import { describe, expect, it } from 'vitest'
import { parseJsonConfig, parseJsonObjectConfig, parseJsonRecordConfig, stripUtf8Bom } from '../../shared/jsonConfig'

describe('jsonConfig', () => {
  describe('stripUtf8Bom', () => {
    it('removes a leading UTF-8 BOM', () => {
      expect(stripUtf8Bom('\uFEFF{"Model":"gpt-5"}')).toBe('{"Model":"gpt-5"}')
    })

    it('returns the original content when no BOM exists', () => {
      expect(stripUtf8Bom('{"Model":"gpt-5"}')).toBe('{"Model":"gpt-5"}')
    })

    it('returns empty input unchanged', () => {
      expect(stripUtf8Bom('')).toBe('')
    })
  })

  describe('parseJsonConfig', () => {
    it('parses JSON object with UTF-8 BOM', () => {
      expect(parseJsonConfig('\uFEFF{"Model":"gpt-5"}', {} as Record<string, unknown>)).toEqual({
        Model: 'gpt-5'
      })
    })

    it('returns fallback for empty input', () => {
      const fallback = { Model: 'Default' }
      expect(parseJsonConfig('   ', fallback)).toBe(fallback)
    })

    it('returns fallback for non-object JSON', () => {
      const fallback = { EndPoint: 'https://example.com/v1' }
      expect(parseJsonConfig('["a", "b"]', fallback)).toBe(fallback)
      expect(parseJsonConfig('"value"', fallback)).toBe(fallback)
    })

    it('returns fallback for invalid JSON', () => {
      const fallback = { Enabled: false }
      expect(parseJsonConfig('{invalid-json', fallback)).toBe(fallback)
    })
  })

  describe('parseJsonObjectConfig', () => {
    it('parses JSON object with UTF-8 BOM', () => {
      expect(parseJsonObjectConfig('\uFEFF{"Model":"gpt-5"}')).toEqual({
        Model: 'gpt-5'
      })
    })

    it('returns an empty object for empty input', () => {
      expect(parseJsonObjectConfig('   ')).toEqual({})
    })

    it('throws for non-object JSON', () => {
      expect(() => parseJsonObjectConfig('["a", "b"]')).toThrow('Config payload must be a JSON object')
      expect(() => parseJsonObjectConfig('"value"')).toThrow('Config payload must be a JSON object')
    })

    it('throws for invalid JSON', () => {
      expect(() => parseJsonObjectConfig('{invalid-json')).toThrow()
    })
  })

  describe('parseJsonRecordConfig', () => {
    it('parses JSON object with UTF-8 BOM', () => {
      expect(parseJsonRecordConfig('\uFEFF{"Model":"gpt-5"}')).toEqual({
        Model: 'gpt-5'
      })
    })

    it('returns an empty object for empty input', () => {
      expect(parseJsonRecordConfig('   ')).toEqual({})
    })

    it('returns an empty object for non-object JSON', () => {
      expect(parseJsonRecordConfig('["a", "b"]')).toEqual({})
      expect(parseJsonRecordConfig('"value"')).toEqual({})
      expect(parseJsonRecordConfig('42')).toEqual({})
      expect(parseJsonRecordConfig('null')).toEqual({})
    })

    it('throws for invalid JSON', () => {
      expect(() => parseJsonRecordConfig('{invalid-json')).toThrow()
    })
  })
})
