// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { DiffViewer } from '../components/detail/DiffViewer'
import { useUIStore } from '../stores/uiStore'
import type { FileDiff } from '../types/toolCall'
import { installDesktopApiMock } from './desktopApiMock'

function makeDiff(overrides: Partial<FileDiff> = {}): FileDiff {
  return {
    filePath: 'src/a.ts',
    turnId: 'turn-1',
    turnIds: ['turn-1'],
    additions: 1,
    deletions: 1,
    diffHunks: [
      {
        oldStart: 3,
        oldLines: 2,
        newStart: 3,
        newLines: 2,
        lines: [
          { type: 'context', content: 'const shared = true' },
          { type: 'remove', content: 'const total = oldValue' },
          { type: 'add', content: 'const total = newValue' }
        ]
      }
    ],
    status: 'written',
    isNewFile: false,
    ...overrides
  }
}

function renderDiff(props: Partial<Parameters<typeof DiffViewer>[0]> = {}): ReturnType<typeof render> {
  return render(
    <LocaleProvider>
      <DiffViewer diff={makeDiff()} workspacePath="F:/work" {...props} />
    </LocaleProvider>
  )
}

function markedText(container: HTMLElement): string[] {
  return [...container.querySelectorAll('[data-diff-span]')].map((node) => node.textContent ?? '')
}

describe('DiffViewer word-level changes', () => {
  beforeEach(() => {
    useUIStore.setState({ diffMarkers: 'color' })
    installDesktopApiMock({ settings: { get: () => Promise.resolve({ locale: 'en' }) } })
  })

  it('marks only the words that differ between a removal and its replacement', () => {
    const { container } = renderDiff()

    expect(markedText(container)).toEqual(['oldValue', 'newValue'])
  })

  it('gives each mark the side that decides its tint, with no pane to inherit from', () => {
    const { container } = renderDiff()

    const sides = [...container.querySelectorAll('[data-diff-span]')]
      .map((span) => span.closest('[data-diff-side]')?.getAttribute('data-diff-side'))
    expect(sides).toEqual(['deletion', 'addition'])
  })

  it('marks the same words in split mode, each on its own side', () => {
    const { container } = renderDiff({ mode: 'split' })

    const left = screen.getByTestId('split-left-pane')
    const right = screen.getByTestId('split-right-pane')
    expect(markedText(left)).toEqual(['oldValue'])
    expect(markedText(right)).toEqual(['newValue'])
    expect(container.querySelector('[data-diff-side="deletion"]')).toBe(left)
    expect(container.querySelector('[data-diff-side="addition"]')).toBe(right)
  })

  it('renders every hunk line with a row identity find can address', () => {
    const { container } = renderDiff()

    const rows = [...container.querySelectorAll('[data-line]')]
    expect(rows.map((row) => row.textContent)).toEqual([
      'const shared = true',
      'const total = oldValue',
      'const total = newValue'
    ])
    expect(new Set(rows.map((row) => row.getAttribute('data-line'))).size).toBe(3)
  })

  it('keeps gutter numbers out of searchable text', () => {
    const { container } = renderDiff()

    const gutters = [...container.querySelectorAll('[data-line-num]')]
    expect(gutters.length).toBeGreaterThan(0)
    for (const gutter of gutters) {
      expect(gutter.closest('[data-line]')).toBeNull()
    }
  })

  it('reports an empty diff instead of rendering an empty body', () => {
    renderDiff({ diff: makeDiff({ diffHunks: [] }) })

    expect(screen.getByText('No changes')).toBeInTheDocument()
    expect(screen.queryByTestId('unified-diff-body')).toBeNull()
  })
})
