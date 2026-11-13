import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { TurnChangesCard } from '../components/conversation/TurnCompletionSummary'
import { LocaleProvider } from '../contexts/LocaleContext'
import type { TurnFileChange } from '../types/turnDiff'
import { installDesktopApiMock } from './desktopApiMock'

function row(id: string, filePath: string, truncated = false): TurnFileChange {
  return {
    key: `turn-1::${id}`,
    turnId: 'turn-1',
    patchText: `patch-${id}`,
    truncated,
    diff: {
      filePath,
      additions: 1,
      deletions: 1,
      status: 'written',
      isNewFile: false,
      diffHunks: [{
        oldStart: 1, oldLines: 1, newStart: 1, newLines: 1,
        lines: [{ type: 'remove', content: `before-${id}` }, { type: 'add', content: `after-${id}` }]
      }]
    }
  }
}

describe('turn file presentation', () => {
  it('groups repeated edits by file for disclosure, expansion and Review', () => {
    installDesktopApiMock({ initialLocale: 'en' })
    const filePath = 'docs/report.md'
    const rows = [
      row('a', filePath, true), row('b', filePath, true), row('c', filePath),
      row('d', 'docs/guide.md'), row('e', 'src/app.ts'), row('f', 'src/other.ts')
    ]
    const review = vi.fn()

    render(<LocaleProvider loadSettings={false}>
      <TurnChangesCard turnId="turn-1" rows={rows} workspacePath="/workspace/demo" onReview={review} />
    </LocaleProvider>)

    expect(screen.getAllByRole('listitem')).toHaveLength(3)
    const fileLink = screen.getByRole('button', { name: filePath })
    fireEvent.click(fileLink)
    expect(review).toHaveBeenCalledWith('turn-1::a')

    const item = fileLink.closest<HTMLElement>('[role="listitem"]')!
    fireEvent.click(within(item).getByRole('button', { expanded: false }))
    expect(within(item).getAllByTestId('inline-diff-view')).toHaveLength(1)
    expect(within(item).getByText('after-c')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Show 1 more file' }))
    expect(screen.getAllByRole('listitem')).toHaveLength(4)
  })
})
