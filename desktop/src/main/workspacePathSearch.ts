import type { FileIndexEntry } from './workspaceFileIndexTypes'

type PathMatchKind = 'file' | 'dir'

export interface PathSearchMatch {
  kind: PathMatchKind
  name: string
  relativePath: string
  dir: string
  positions: number[]
}

export interface PathSearchOptions {
  boostedPaths?: ReadonlySet<string>
  includeDirectories?: boolean
}

const SCORE_MATCH = 16
const BONUS_BOUNDARY = 8
const BONUS_CAMEL = 6
const BONUS_CONSECUTIVE = 4
const BONUS_FIRST_CHAR = 8
const PENALTY_GAP_START = 3
const PENALTY_GAP_EXTENSION = 1
const BONUS_NAME_MATCH = 12
const BONUS_EXACT_NAME = 40
const BONUS_BOOSTED = 14
const PENALTY_TEST_PATH = 10

const TEST_PATH = /(^|\/)(tests?|__tests__)\/|\.(test|spec)\.[^/]+$/i

interface IndexedPath {
  kind: PathMatchKind
  relativePath: string
  name: string
  dir: string
  haystack: string
  lower: string
  lowerName: string
  nameStart: number
  letters: number
  penalty: number
}

interface RankedPath {
  entry: IndexedPath
  score: number
  positions: number[]
}

export class PathSearchIndex {
  private readonly paths: IndexedPath[] = []
  private readonly dirsByLowerPath = new Map<string, IndexedPath>()
  private readonly childrenByDir = new Map<string, IndexedPath[]>()

  constructor(files: readonly FileIndexEntry[]) {
    const dirs = new Set<string>()
    for (const file of files) {
      let dir = file.dir
      while (dir && !dirs.has(dir)) {
        dirs.add(dir)
        const slash = dir.lastIndexOf('/')
        dir = slash < 0 ? '' : dir.slice(0, slash)
      }
    }
    for (const dir of dirs) {
      const slash = dir.lastIndexOf('/')
      this.add('dir', dir, dir.slice(slash + 1), slash < 0 ? '' : dir.slice(0, slash))
    }
    for (const file of files) this.add('file', file.relativePath, file.name, file.dir)
    for (const children of this.childrenByDir.values()) children.sort(compareListing)
  }

  list(dir: string, limit: number, includeDirectories = true): PathSearchMatch[] {
    const children = this.childrenByDir.get(dir) ?? []
    return (includeDirectories ? children : children.filter((entry) => entry.kind === 'file'))
      .slice(0, limit)
      .map((entry) => toMatch(entry, []))
  }

  search(rawQuery: string, limit: number, options: PathSearchOptions = {}): PathSearchMatch[] {
    const needle = rawQuery.trim().replace(/\\/g, '/').toLowerCase()
    const includeDirectories = options.includeDirectories ?? false
    if (!needle) return this.list('', limit, includeDirectories)
    const pinned = this.drillDownEntries(needle, limit, includeDirectories)
    const pinnedKeys = new Set(pinned.map(matchKey))
    const ranked = this.rank(needle, limit + pinned.length, options.boostedPaths, includeDirectories)
      .filter((match) => !pinnedKeys.has(matchKey(match)))
    return [...pinned, ...ranked].slice(0, limit)
  }

  private add(kind: PathMatchKind, relativePath: string, name: string, dir: string): void {
    const haystack = kind === 'dir' ? relativePath + '/' : relativePath
    const lower = haystack.toLowerCase()
    const entry: IndexedPath = {
      kind,
      relativePath,
      name,
      dir,
      haystack,
      lower,
      lowerName: name.toLowerCase(),
      nameStart: relativePath.length - name.length,
      letters: letterBits(lower),
      penalty: TEST_PATH.test(haystack) ? PENALTY_TEST_PATH : 0
    }
    this.paths.push(entry)
    if (kind === 'dir') this.dirsByLowerPath.set(lower, entry)
    const siblings = this.childrenByDir.get(dir)
    if (siblings) siblings.push(entry)
    else this.childrenByDir.set(dir, [entry])
  }

  private drillDownEntries(needle: string, limit: number, includeDirectories: boolean): PathSearchMatch[] {
    const self = includeDirectories ? this.dirsByLowerPath.get(needle) : undefined
    if (!self) return []
    const positions = Array.from({ length: self.haystack.length }, (_, index) => index)
    return [toMatch(self, positions), ...this.list(self.relativePath, limit - 1)]
  }

  private rank(
    needle: string,
    limit: number,
    boosted: ReadonlySet<string> | undefined,
    includeDirectories: boolean
  ): PathSearchMatch[] {
    const needleLetters = letterBits(needle)
    const top: RankedPath[] = []
    for (const entry of this.paths) {
      if (!includeDirectories && entry.kind === 'dir') continue
      if ((entry.letters & needleLetters) !== needleLetters) continue
      const scored = scoreEntry(entry, needle)
      if (!scored) continue
      let score = scored.score - entry.penalty
      if (boosted?.has(entry.relativePath)) score += BONUS_BOOSTED
      const candidate = { entry, score, positions: scored.positions }
      if (top.length === limit && compareRanked(candidate, top[top.length - 1]!) >= 0) continue
      let at = top.length
      while (at > 0 && compareRanked(candidate, top[at - 1]!) < 0) at -= 1
      top.splice(at, 0, candidate)
      if (top.length > limit) top.pop()
    }
    return top.map(({ entry, positions }) => toMatch(entry, positions))
  }
}

function matchKey(match: PathSearchMatch): string {
  return match.kind + ':' + match.relativePath
}

function compareRanked(a: RankedPath, b: RankedPath): number {
  return b.score - a.score ||
    a.entry.haystack.length - b.entry.haystack.length ||
    compareText(a.entry.lower, b.entry.lower)
}

function compareListing(a: IndexedPath, b: IndexedPath): number {
  if (a.kind !== b.kind) return a.kind === 'dir' ? -1 : 1
  return compareText(a.lowerName, b.lowerName)
}

// Ordinal comparison: locale-aware collation is too slow for indexes with hundreds of thousands of paths.
function compareText(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0
}

function toMatch(entry: IndexedPath, positions: number[]): PathSearchMatch {
  return {
    kind: entry.kind,
    name: entry.name,
    relativePath: entry.relativePath,
    dir: entry.dir,
    positions: positions.filter((at) => at < entry.relativePath.length)
  }
}

function scoreEntry(entry: IndexedPath, needle: string): { score: number; positions: number[] } | null {
  const whole = matchFrom(entry.lower, needle, 0, entry.lower.length)
  let best = whole ? { score: scorePositions(entry, whole), positions: whole } : null

  // Greedy matching over the whole path can spend query characters on folder names,
  // so also try anchoring the text after the last `/` to the entry's own name.
  const slash = needle.lastIndexOf('/')
  const tail = needle.slice(slash + 1)
  if (tail) {
    const head = needle.slice(0, slash + 1)
    const headPositions = head ? matchFrom(entry.lower, head, 0, entry.nameStart) : []
    const tailPositions = headPositions && matchFrom(entry.lower, tail, entry.nameStart, entry.lower.length)
    if (headPositions && tailPositions) {
      const positions = [...headPositions, ...tailPositions]
      const score = scorePositions(entry, positions) + BONUS_NAME_MATCH
      if (!best || score > best.score) best = { score, positions }
    }
  }
  if (!best) return null

  const lowerName = entry.lowerName
  const stem = entry.kind === 'file' ? lowerName.replace(/\.[^.]+$/, '') : lowerName
  if (tail && (lowerName === tail || stem === tail)) best.score += BONUS_EXACT_NAME
  return best
}

function matchFrom(haystack: string, needle: string, start: number, end: number): number[] | null {
  const positions: number[] = []
  let from = start
  for (const char of needle) {
    const at = haystack.indexOf(char, from)
    if (at < 0 || at >= end) return null
    positions.push(at)
    from = at + char.length
  }
  return positions
}

function scorePositions(entry: IndexedPath, positions: number[]): number {
  const text = entry.haystack
  let score = positions.length * SCORE_MATCH
  for (let index = 0; index < positions.length; index += 1) {
    const at = positions[index]!
    if (index > 0) {
      const gap = at - positions[index - 1]! - 1
      score += gap === 0 ? BONUS_CONSECUTIVE : -(PENALTY_GAP_START + gap * PENALTY_GAP_EXTENSION)
    }
    score += boundaryBonus(text, at, index === 0)
  }
  return score + Math.max(0, 32 - (text.length >> 2))
}

function boundaryBonus(text: string, at: number, first: boolean): number {
  if (at === 0) return first ? BONUS_FIRST_CHAR : 0
  const previous = text[at - 1]!
  if ('/-_. '.includes(previous)) return BONUS_BOUNDARY
  const current = text[at]!
  return previous >= 'a' && previous <= 'z' && current >= 'A' && current <= 'Z' ? BONUS_CAMEL : 0
}

function letterBits(text: string): number {
  let bits = 0
  for (let index = 0; index < text.length; index += 1) {
    const code = text.charCodeAt(index)
    if (code >= 97 && code <= 122) bits |= 1 << (code - 97)
  }
  return bits
}
