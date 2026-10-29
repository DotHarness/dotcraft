interface Fragment {
  start: number
  end: number
}

const ALNUM = /[\p{L}\p{N}]/u
const DIGIT = /\p{N}/u

function isWordStart(text: string, index: number): boolean {
  const char = text[index]
  if (!ALNUM.test(char)) return false
  if (index === 0) return true
  const previous = text[index - 1]
  if (!ALNUM.test(previous)) return true
  if (char !== char.toLowerCase() && previous !== previous.toUpperCase()) return true
  return DIGIT.test(char) && !DIGIT.test(previous)
}

function matchFragments(text: string, lower: string, query: string, start: number): Fragment[] | null {
  const failed = new Set<number>()
  const extend = (queryIndex: number, position: number, fragmentStart: number): Fragment[] | null => {
    if (queryIndex === query.length) return [{ start: fragmentStart, end: position }]
    const key = queryIndex * (lower.length + 1) + position
    if (failed.has(key)) return null
    if (lower[position] === query[queryIndex]) {
      const contiguous = extend(queryIndex + 1, position + 1, fragmentStart)
      if (contiguous) return contiguous
    }
    for (let next = position + 1; next < lower.length; next += 1) {
      if (lower[next] !== query[queryIndex] || !isWordStart(text, next)) continue
      const rest = extend(queryIndex + 1, next + 1, next)
      if (rest) return [{ start: fragmentStart, end: position }, ...rest]
    }
    failed.add(key)
    return null
  }
  return extend(1, start + 1, start)
}

function degree(text: string, fragments: Fragment[]): number {
  const first = fragments[0]
  const last = fragments[fragments.length - 1]
  let value = -fragments.length
  if (first.start === 0) value += 10000
  if (isWordStart(text, first.start)) value += 1000
  if (last.end === text.length) value += 1
  for (let index = 1; index < fragments.length; index += 1) {
    for (let skipped = fragments[index - 1].end; skipped < fragments[index].start; skipped += 1) {
      if (isWordStart(text, skipped)) value -= 10
    }
  }
  return value
}

export function fuzzyScore(text: string, rawQuery: string): number {
  const query = rawQuery.replace(/\s+/g, '').toLowerCase()
  if (!query || text.length < query.length) return 0
  const lower = text.toLowerCase()
  let best = 0
  for (let start = lower.indexOf(query[0]); start >= 0; start = lower.indexOf(query[0], start + 1)) {
    const fragments = matchFragments(text, lower, query, start)
    if (!fragments) continue
    const head = fragments[0]
    if (!isWordStart(text, head.start) && head.end - head.start < 3) continue
    best = Math.max(best, Math.max(1, degree(text, fragments) * 10 - text.length))
  }
  return best
}

function bestFuzzyScore(fields: readonly string[], query: string): number {
  return fields.reduce((best, field) => Math.max(best, fuzzyScore(field, query)), 0)
}

export function fuzzyFilter<T>(items: readonly T[], query: string, fields: (item: T) => readonly string[]): T[] {
  if (!query.trim()) return [...items]
  return items
    .map((item) => ({ item, score: bestFuzzyScore(fields(item), query) }))
    .filter((entry) => entry.score > 0)
    .sort((left, right) => right.score - left.score)
    .map((entry) => entry.item)
}

export function fuzzyRank<T>(items: readonly T[], query: string, fields: (item: T) => readonly string[]): T[] {
  return items
    .map((item, index) => ({ item, index, score: Math.max(1, bestFuzzyScore(fields(item), query)) }))
    .sort((left, right) => right.score - left.score || left.index - right.index)
    .map((entry) => entry.item)
}

export function fuzzyHighlightRanges(text: string, rawQuery: string): Fragment[] {
  const query = rawQuery.replace(/\s+/g, '').toLowerCase()
  if (!query) return []
  const lower = text.toLowerCase()
  const at = lower.indexOf(query)
  if (at >= 0) return [{ start: at, end: at + query.length }]
  const ranges: Fragment[] = []
  let queryIndex = 0
  for (let index = 0; index < lower.length && queryIndex < query.length; index += 1) {
    if (lower[index] !== query[queryIndex]) continue
    queryIndex += 1
    const last = ranges[ranges.length - 1]
    if (last && last.end === index) last.end = index + 1
    else ranges.push({ start: index, end: index + 1 })
  }
  return queryIndex === query.length ? ranges : []
}
