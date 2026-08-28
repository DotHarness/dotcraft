/**
 * The defect this stack exists to fix: a construct that spans lines used to
 * lose its coloring from the second line on, because the previous
 * implementation ran the highlighter once per line.
 */
import { describe, expect, it } from 'vitest'
import { createHighlighter, installGrammars } from '../highlight/core'
import { buildDiffSides } from '../highlight/diffSides'
import { executeDiff, executeFile } from '../highlight/execute'
import { prepareDiff, prepareFile } from '../highlight/prepare'
import type {
  DiffHighlightRequest,
  DiffHighlightResult,
  FileHighlightRequest,
  FileHighlightResult,
  HighlightedLine
} from '../highlight/types'
import type { FileDiff } from '../types/toolCall'

/** The full path a request takes: resolve grammars, install them, tokenize. */
async function highlightFile(request: FileHighlightRequest): Promise<FileHighlightResult> {
  const highlighter = createHighlighter()
  const prepared = await prepareFile(request)
  installGrammars(highlighter, prepared.grammars)
  return executeFile(highlighter.core, request, prepared.lang)
}

async function highlightDiff(request: DiffHighlightRequest): Promise<DiffHighlightResult> {
  const highlighter = createHighlighter()
  const prepared = await prepareDiff(request)
  installGrammars(highlighter, prepared.grammars)
  return executeDiff(highlighter.core, request, prepared.deletionLang, prepared.additionLang)
}

function text(line: HighlightedLine | undefined): string {
  return (line ?? []).map((span) => span.text).join('')
}

/** The light-theme color assigned to every run of a line, deduplicated. */
function colors(line: HighlightedLine | undefined): string[] {
  return [...new Set((line ?? [])
    .filter((span) => span.text.trim().length > 0)
    .map((span) => span.style?.['--dc-token-light'] ?? ''))]
}

describe('diff tokenization', () => {
  it('keeps a block comment colored past its first line', async () => {
    const result = await highlightFile({
      name: 'Sample.cs',
      contents: '/* one\n two\n three */\nvar x = 1;\n'
    })

    expect(result.highlighted).toBe(true)
    // All three comment lines share one color; the old per-line pass gave the
    // second and third whatever the grammar's initial state produced instead.
    const commentColors = [0, 1, 2].flatMap((index) => colors(result.lines[index]))
    expect(new Set(commentColors).size).toBe(1)
    // And the line after the comment is not colored as a comment.
    expect(colors(result.lines[3])).not.toEqual(commentColors.slice(0, 1))
  })

  it('colors both versions of a changed multi-line construct', async () => {
    // The comment's opener is on a removed line and its closer on a shared
    // context line. Tokenizing the hunk's rows in display order would splice the
    // added line into the middle of the comment.
    const original = 'var a = 1;\n/* note\n   about a\n*/\nvar b = 2;\n'
    const current = 'var a = 1;\n/* remark\n   about a\n*/\nvar b = 2;\n'
    const diff: FileDiff = {
      filePath: 'F:/work/Sample.cs',
      turnId: 't',
      turnIds: ['t'],
      additions: 1,
      deletions: 1,
      status: 'written',
      isNewFile: false,
      originalContent: original,
      currentContent: current,
      diffHunks: [{
        oldStart: 1,
        oldLines: 5,
        newStart: 1,
        newLines: 5,
        lines: [
          { type: 'context', content: 'var a = 1;' },
          { type: 'remove', content: '/* note' },
          { type: 'add', content: '/* remark' },
          { type: 'context', content: '   about a' },
          { type: 'context', content: '*/' },
          { type: 'context', content: 'var b = 2;' }
        ]
      }]
    }

    const sides = buildDiffSides(diff)
    const result = await highlightDiff({
      name: diff.filePath,
      deletion: sides.deletion,
      addition: sides.addition
    })

    expect(result.highlighted).toBe(true)
    expect(text(result.deletion[1])).toBe('/* note')
    expect(text(result.addition[1])).toBe('/* remark')
    // The comment body and terminator are inside the comment on both sides.
    for (const side of [result.deletion, result.addition]) {
      expect(colors(side[1])).toEqual(colors(side[2]))
      expect(colors(side[2])).toEqual(colors(side[3]))
      expect(colors(side[4])).not.toEqual(colors(side[1]))
    }
  })

  it('derives each side language from its own path, so a renamed extension still colors', async () => {
    const result = await highlightDiff({
      name: 'notes.json',
      prevName: 'notes.txt',
      deletion: [{ text: '{"a": 1}', lineIndices: [0] }],
      addition: [{ text: '{"a": 2}', lineIndices: [0] }]
    })

    // .txt has no grammar, so the old side stays plain; the new side is JSON.
    expect(colors(result.deletion[0])).toEqual([''])
    expect(colors(result.addition[0]).some((color) => color.length > 0)).toBe(true)
  })

  it('renders an unknown language as plain text rather than failing', async () => {
    const result = await highlightFile({
      name: 'notes.unknownext',
      contents: 'first\nsecond\n'
    })

    expect(result.highlighted).toBe(false)
    expect(result.lines.map(text)).toEqual(['first', 'second', ''])
  })

})
