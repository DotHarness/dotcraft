import { describe, expect, it } from 'vitest'
import { parsePlanMarkdown } from '../utils/planMarkdown'

describe('parsePlanMarkdown', () => {
  it('extracts H1 title, first section overview, and content without the H1', () => {
    const parsed = parsePlanMarkdown(`# Streaming Plan

## 概览

第一段概要。
第二行继续。

第二段不应进入概要。

## 验证方案

- 运行测试。`)

    expect(parsed.title).toBe('Streaming Plan')
    expect(parsed.overview).toBe('第一段概要。 第二行继续。')
    expect(parsed.content.startsWith('# Streaming Plan')).toBe(false)
    expect(parsed.content).toContain('## 概览')
    expect(parsed.content).toContain('## 验证方案')
  })

  it('uses first non-empty line as title and following paragraph as overview without H1', () => {
    const parsed = parsePlanMarkdown(`Implement cache invalidation

Keep the existing storage format.

## Changes

- Add targeted invalidation.`)

    expect(parsed.title).toBe('Implement cache invalidation')
    expect(parsed.overview).toBe('Keep the existing storage format.')
    expect(parsed.content).toContain('Implement cache invalidation')
  })

  it('skips empty sections when extracting overview', () => {
    const parsed = parsePlanMarkdown(`# 计划

## 空章节

## 背景

使用结构而不是英文标题识别概要。`)

    expect(parsed.overview).toBe('使用结构而不是英文标题识别概要。')
  })
})
