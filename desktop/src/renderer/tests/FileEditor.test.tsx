import { act } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { renderMermaidSvg } from '../components/conversation/MermaidDiagram'
import { canPreviewFileReview } from '../components/detail/viewers/FileReview'
import { useFileEditorStore } from '../stores/fileEditorStore'
import { installDesktopApiMock } from './desktopApiMock'
import type { ReadTextResult, WriteTextParams } from '../../shared/viewer/types'

vi.mock('../components/conversation/MermaidDiagram', () => ({
  renderMermaidSvg: vi.fn(async () => '<svg aria-label="diagram"></svg>')
}))

const store = () => useFileEditorStore.getState()
const markdown =
  '# Heading\n\n**bold** and *italic* and ~~gone~~ with `inline`\n\n> quote\n\n- [x] item\n\n| A | B |\n| --- | --- |\n| x | y |\n\n```ts\nconst a = 1\n```\n\n```mermaid\nflowchart LR\n  A --> B\n```'

describe('FileEditor integration', () => {
  let disk: ReadTextResult
  const writeText = vi.fn()
  beforeEach(() => {
    vi.mocked(renderMermaidSvg).mockClear()
    disk = {
      text: markdown,
      truncated: false,
      encoding: 'utf-8',
      mtimeMs: 1,
      sizeBytes: markdown.length,
      hasUtf8Bom: false,
      lineEnding: 'lf'
    }
    writeText.mockReset().mockImplementation(async (request: WriteTextParams) => {
      disk = { ...disk, text: request.text, mtimeMs: disk.mtimeMs + 1 }
      return { outcome: 'saved', mtimeMs: disk.mtimeMs, sizeBytes: disk.text.length }
    })
    installDesktopApiMock({
      workspace: {
        viewer: {
          readText: vi.fn(async () => ({ ...disk })),
          writeText,
          watchText: vi.fn(async () => () => {})
        }
      }
    })
    Range.prototype.getClientRects = () => [] as unknown as DOMRectList
    Range.prototype.getBoundingClientRect = () => new DOMRect()
  })
  afterEach(() => {
    act(() => {
      for (const id of store().sessions.keys()) store().discard(id)
    })
  })

  it('bounds review by UTF-8 bytes and line count, keeping boundary values previewable', () => {
    expect(canPreviewFileReview('a'.repeat(256 * 1024))).toBe(true)
    expect(canPreviewFileReview('a'.repeat(256 * 1024 + 1))).toBe(false)
    expect(canPreviewFileReview('文'.repeat(90000))).toBe(false)
    expect(canPreviewFileReview(Array(5000).fill('a').join('\n'))).toBe(true)
    expect(canPreviewFileReview(Array(5001).fill('a').join('\n'))).toBe(false)
  })
})
