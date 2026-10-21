import { createTwoFilesPatch, parsePatch } from 'diff'
import { describe, expect, it } from 'vitest'
import { composeFilePatches } from './composeFilePatches'

const lines = (count: number, edit: Record<number, string> = {}) =>
  Array.from({ length: count }, (_, index) => `${edit[index] ?? `line ${index + 1}`}\n`).join('')

function gitPatch(before: string, after: string, created = false): string {
  const body = createTwoFilesPatch(created ? '/dev/null' : 'a/src/app.ts', 'b/src/app.ts', before, after, '', '', { context: 3 })
  const hunks = body.slice(body.indexOf('@@'))
  return `diff --git a/src/app.ts b/src/app.ts\n${created ? 'new file mode 100644\n--- /dev/null' : '--- a/src/app.ts'}\n+++ b/src/app.ts\n${hunks}`
}

const hunksOf = (patch: string) => parsePatch(patch)[0]!.hunks

describe('composeFilePatches', () => {
  it('composes sequential edits into the diff from the first version to the last', () => {
    const v0 = lines(40)
    const v1 = lines(40, { 2: 'changed 3' })
    const v2 = lines(40, { 2: 'changed 3', 30: 'changed 31' })
    const v3 = lines(40, { 2: 'changed again', 30: 'changed 31' })

    const composed = composeFilePatches([gitPatch(v0, v1), gitPatch(v1, v2), gitPatch(v2, v3)])

    expect(composed).toMatch(/^diff --git a\/src\/app.ts b\/src\/app.ts\n--- a\/src\/app.ts\n\+\+\+ b\/src\/app.ts\n@@/)
    expect(hunksOf(composed!)).toEqual(hunksOf(gitPatch(v0, v3)))
  })

  it('keeps a created file as one addition and drops edits that cancel out', () => {
    const created = composeFilePatches([gitPatch('', 'a\nb\n', true), gitPatch('a\nb\n', 'a\nc\n')])
    expect(hunksOf(created!)).toEqual(hunksOf(gitPatch('', 'a\nc\n', true)))
    expect(created).toContain('new file mode 100644')

    const v0 = lines(10)
    const v1 = lines(10, { 4: 'tmp' })
    expect(composeFilePatches([gitPatch(v0, v1), gitPatch(v1, v0)])).toBe('')
  })

  it('refuses patches that do not apply in sequence', () => {
    const v0 = lines(10)
    expect(composeFilePatches([gitPatch(v0, lines(10, { 1: 'x' })), gitPatch(lines(10, { 1: 'y' }), v0)])).toBeNull()
  })
})
