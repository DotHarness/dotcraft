import { formatPatch, parsePatch, structuredPatch, type StructuredPatch, type StructuredPatchHunk } from 'diff'

/** A run of lines in the composed file: new text, or lines kept from the original file by index. */
type Segment = string | { start: number; count: number }

const DEV_NULL = '/dev/null'

/**
 * Composes one file's sequential per-call git diffs into a single diff from before the first call
 * to after the last. Returns '' when the edits cancel out and null when the patches cannot be
 * composed (not sequential, or not plain edits of one file).
 */
export function composeFilePatches(patches: readonly string[]): string | null {
  try {
    const parsed = patches.map((patch) => {
      const files = parsePatch(patch)
      if (files.length !== 1) throw new Error('Expected one file patch')
      return files[0]!
    })
    const first = parsed[0]
    if (!first || parsed.some((patch, index) => !isPlainEdit(patch, index))) return null

    const created = first.oldFileName === DEV_NULL
    const segments: Segment[] = created ? [] : [{ start: 0, count: Infinity }]
    const original = new Map<number, string>()
    for (const patch of parsed) applyHunks(patch.hunks, segments, original)

    const hunks = rediff(segments, original, created)
    const header = headerOf(patches[0]!)
    if (hunks.length === 0) return created ? header + '\n' : ''
    const formatted = formatPatch({ ...first, hunks } as StructuredPatch)
    return `${header}\n${formatted.slice(formatted.search(/^@@/m))}`
  } catch {
    return null
  }
}

function isPlainEdit(patch: StructuredPatch, index: number): boolean {
  const created = patch.oldFileName === DEV_NULL
  return patch.newFileName !== DEV_NULL && (!created || index === 0) && stripSide(patch.oldFileName) !== null
    && (created || stripSide(patch.oldFileName) === stripSide(patch.newFileName))
}

function stripSide(name: string | undefined): string | null {
  if (!name) return null
  return name === DEV_NULL ? name : name.replace(/^[ab]\//, '')
}

function headerOf(patch: string): string {
  const hunkStart = patch.search(/^@@/m)
  return (hunkStart < 0 ? patch : patch.slice(0, hunkStart)).replace(/[\r\n]+$/, '')
}

function applyHunks(hunks: StructuredPatchHunk[], segments: Segment[], original: Map<number, string>): void {
  let shift = 0
  for (const hunk of hunks) {
    const position = hunk.oldStart - 1 + shift
    const from = splitAt(segments, position)
    const to = splitAt(segments, position + hunk.oldLines)
    const current: Array<string | number> = segments.slice(from, to).flatMap((segment): Array<string | number> =>
      typeof segment === 'string' ? [segment] : Array.from({ length: segment.count }, (_, offset) => segment.start + offset))
    const replacement: Segment[] = []
    let consumed = 0
    hunk.lines.forEach((line, index) => {
      if (line.startsWith('\\')) return
      const text = line.slice(1) + (hunk.lines[index + 1]?.startsWith('\\') ? '' : '\n')
      if (line.startsWith('+')) {
        replacement.push(text)
        return
      }
      const existing = current[consumed++]
      if (typeof existing === 'number') {
        const known = original.get(existing)
        if (known != null && known !== text) throw new Error('Patches disagree about the original content')
        original.set(existing, text)
      } else if (existing !== text) {
        throw new Error('Patches are not sequential')
      }
      if (line.startsWith(' ')) replacement.push(typeof existing === 'number' ? { start: existing, count: 1 } : text)
    })
    if (consumed !== current.length) throw new Error('Patch length mismatch')
    segments.splice(from, to - from, ...replacement)
    shift += hunk.newLines - hunk.oldLines
  }
}

/** Returns the segment index where line `position` begins, splitting a kept range when needed. */
function splitAt(segments: Segment[], position: number): number {
  if (position < 0) throw new Error('Patch starts before the file')
  let remaining = position
  for (let index = 0; index < segments.length; index++) {
    if (remaining === 0) return index
    const segment = segments[index]!
    const length = typeof segment === 'string' ? 1 : segment.count
    if (remaining < length && typeof segment !== 'string') {
      segments.splice(index, 1, { start: segment.start, count: remaining }, { start: segment.start + remaining, count: segment.count - remaining })
      return index + 1
    }
    remaining -= length
  }
  if (remaining === 0) return segments.length
  throw new Error('Patch lies outside the known file')
}

function rediff(segments: Segment[], original: Map<number, string>, created: boolean): StructuredPatchHunk[] {
  const hunks: StructuredPatchHunk[] = []
  let oldCursor = 0
  let newCursor = 0
  let pending = ''
  let pendingLines = 0
  const flush = (end: number): void => {
    let before = ''
    for (let index = oldCursor; index < end; index++) {
      const text = original.get(index)
      if (text == null) throw new Error('Missing original content')
      before += text
    }
    for (const hunk of structuredPatch('', '', before, pending, '', '', { context: 3 }).hunks) {
      hunks.push({ ...hunk, oldStart: hunk.oldStart + oldCursor, newStart: hunk.newStart + newCursor })
    }
    newCursor += pendingLines
    pending = ''
    pendingLines = 0
    oldCursor = end
  }
  for (const segment of segments) {
    if (typeof segment === 'string') {
      pending += segment
      pendingLines++
      continue
    }
    const known = segment.count === 1 ? original.get(segment.start) : undefined
    if (known != null) {
      pending += known
      pendingLines++
      continue
    }
    flush(segment.start)
    oldCursor += segment.count
    newCursor += segment.count
  }
  if (created) flush(0)
  return hunks
}
