import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { InlineDiffView } from '../components/conversation/InlineDiffView'
import type { FileDiff } from '../types/toolCall'

const baseDiff: FileDiff = {
  filePath: 'src/deep/AgentTools.cs',
  turnId: 'turn-1',
  turnIds: ['turn-1'],
  additions: 1,
  deletions: 1,
  diffHunks: [
    {
      oldStart: 35,
      oldLines: 2,
      newStart: 35,
      newLines: 2,
      lines: [
        { type: 'context', content: 'unchanged' },
        { type: 'remove', content: 'old line' },
        { type: 'add', content: 'new line' }
      ]
    }
  ],
  status: 'written',
  isNewFile: false,
  originalContent: 'unchanged\nold line\n',
  currentContent: 'unchanged\nnew line\n'
}

describe('InlineDiffView', () => {
  it('keeps standalone mode bordered by default', () => {
    render(<InlineDiffView diff={baseDiff} />)

    const view = screen.getByTestId('inline-diff-view')
    expect(view.style.borderWidth).toBe('1px')
    expect(view.style.borderStyle).toBe('solid')
    expect(view.style.borderRadius).toBe('4px')
    expect(screen.getByText('src/deep/AgentTools.cs')).toBeInTheDocument()
  })

  it('renders embedded mode without an inner card border', () => {
    render(<InlineDiffView diff={baseDiff} variant="embedded" />)

    const view = screen.getByTestId('inline-diff-view')
    expect(view.style.borderWidth).toBe('0px')
    expect(view.style.borderStyle).toBe('none')
    expect(view.style.borderRadius).toBe('0px')
  })

  it('renders compact headers with basename and full path tooltip', () => {
    render(
      <InlineDiffView
        diff={baseDiff}
        variant="embedded"
        headerMode="compact"
      />
    )

    const filename = screen.getByText('AgentTools.cs')
    expect(filename).toHaveAttribute('title', 'src/deep/AgentTools.cs')
    expect(screen.queryByText('src/deep/AgentTools.cs')).toBeNull()
  })

  it('can hide the streaming text while keeping the live cursor', () => {
    render(
      <InlineDiffView
        diff={baseDiff}
        streaming
        showStreamingIndicator={false}
      />
    )

    expect(screen.queryByText('streaming')).toBeNull()
    expect(screen.getByText('|')).toBeInTheDocument()
  })

  it('does not show a waiting placeholder for empty streaming diffs', () => {
    render(
      <InlineDiffView
        diff={{ ...baseDiff, additions: 0, deletions: 0, diffHunks: [] }}
        streaming
      />
    )

    expect(screen.queryByText('Waiting for content...')).toBeNull()
    expect(screen.queryByText('No changes')).toBeNull()
  })
})
