import { fireEvent, render, screen } from '@testing-library/react'
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
  it('opens Review at the first change of the selected file', () => {
    installDesktopApiMock({ initialLocale: 'en' })
    const filePath = 'docs/report.md'
    const rows = [
      row('a', filePath, true), row('b', filePath), row('c', 'src/app.ts')
    ]
    const review = vi.fn()

    render(<LocaleProvider loadSettings={false}>
      <TurnChangesCard turnIds={['turn-1']} rows={rows} workspacePath="/workspace/demo" onReview={review} />
    </LocaleProvider>)

    fireEvent.click(screen.getByRole('button', { name: filePath }))
    expect(review).toHaveBeenCalledWith('turn-1::a')
  })
})
