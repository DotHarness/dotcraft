import { fireEvent, render, screen } from '@testing-library/react'
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
  it('renders compact headers with basename and full path tooltip', async () => {
    render(
      <InlineDiffView
        diff={baseDiff}
        variant="embedded"
        headerMode="compact"
      />
    )

    const filename = screen.getByText('AgentTools.cs')
    expect(filename).not.toHaveAttribute('title')
    expect(screen.queryByText('src/deep/AgentTools.cs')).toBeNull()

    fireEvent.mouseEnter(filename.parentElement as HTMLElement)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('src/deep/AgentTools.cs')
  })

  it('shows no streaming spinner or label, keeping only the live cursor', () => {
    render(
      <InlineDiffView
        diff={baseDiff}
        streaming
      />
    )

    // The visibly-growing diff (live cursor) is the only running cue; the header
    // no longer renders a spinner or "streaming" label.
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
