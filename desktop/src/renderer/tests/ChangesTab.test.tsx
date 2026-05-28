// @vitest-environment jsdom
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ChangesTab } from '../components/detail/ChangesTab'
import { DiffViewer } from '../components/detail/DiffViewer'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import type { FileDiff } from '../types/toolCall'

const cs = () => useConversationStore.getState()
const ui = () => useUIStore.getState()

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
          { type: 'context', content: 'shared line' },
          { type: 'remove', content: 'old line' },
          { type: 'add', content: 'new line' }
        ]
      }
    ],
    status: 'written',
    isNewFile: false,
    ...overrides
  }
}

function Harness({ workspacePath = 'F:\\work' }: { workspacePath?: string }): JSX.Element {
  return (
    <LocaleProvider>
      <ChangesTab workspacePath={workspacePath} />
    </LocaleProvider>
  )
}

beforeEach(() => {
  cs().reset()
  useThreadStore.setState({ activeThreadId: 'thread-1' })
  useUIStore.setState({
    selectedChangedFile: null,
    changesDiffModeByThread: {},
    activeDetailTab: { kind: 'system', id: 'changes' },
    detailPanelVisible: true,
    detailPanelPreferredVisible: true
  })
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      settings: {
        get: async () => ({ locale: 'en' })
      },
      shell: {
        launchEditor: vi.fn(async () => {}),
        showItemInFolder: vi.fn(async () => {})
      }
    }
  })
})

describe('ChangesTab diff stream', () => {
  it('expands the first file by default and keeps the rest collapsed', async () => {
    cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts' }))
    cs().upsertChangedFile(makeDiff({
      filePath: 'src/b.ts',
      diffHunks: [
        {
          oldStart: 1,
          oldLines: 1,
          newStart: 1,
          newLines: 1,
          lines: [
            { type: 'remove', content: 'second old' },
            { type: 'add', content: 'second new' }
          ]
        }
      ]
    }))

    render(<Harness />)

    await waitFor(() => {
      expect(screen.getByText('old line')).toBeInTheDocument()
    })
    expect(screen.queryByText('second old')).toBeNull()
  })

  it('toggles a file section by clicking its summary row', async () => {
    cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts' }))

    render(<Harness />)

    await waitFor(() => {
      expect(screen.getByText('old line')).toBeInTheDocument()
    })

    fireEvent.click(screen.getByText('src/a.ts'))

    await waitFor(() => {
      expect(screen.queryByText('old line')).toBeNull()
    })
  })

  it('keeps a manually collapsed first file collapsed when diffs refresh', async () => {
    cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts' }))
    cs().upsertChangedFile(makeDiff({
      filePath: 'src/b.ts',
      diffHunks: [
        {
          oldStart: 1,
          oldLines: 1,
          newStart: 1,
          newLines: 1,
          lines: [
            { type: 'remove', content: 'second old' },
            { type: 'add', content: 'second new' }
          ]
        }
      ]
    }))

    render(<Harness />)

    await waitFor(() => {
      expect(screen.getByText('old line')).toBeInTheDocument()
    })

    fireEvent.click(screen.getByText('src/a.ts'))

    await waitFor(() => {
      expect(screen.queryByText('old line')).toBeNull()
    })

    act(() => {
      cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts', additions: 2 }))
    })

    await waitFor(() => {
      expect(screen.queryByText('old line')).toBeNull()
    })
  })

  it('stores split diff mode per thread', async () => {
    cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts' }))

    render(<Harness />)

    fireEvent.click(screen.getByLabelText('Show split diff'))

    expect(ui().getChangesDiffMode('thread-1')).toBe('split')

    act(() => {
      useThreadStore.setState({ activeThreadId: 'thread-2' })
    })

    await waitFor(() => {
      expect(ui().getChangesDiffMode('thread-2')).toBe('inline')
    })
  })

  it('opens the changed file parent directory from the hover action', async () => {
    cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts' }))

    render(<Harness workspacePath={'F:\\work'} />)

    fireEvent.click(screen.getByLabelText('Open containing folder'))

    await waitFor(() => {
      expect(window.api.shell.launchEditor).toHaveBeenCalledWith('explorer', 'F:\\work\\src')
    })
  })

  it('renders Revert All with a single icon-only glyph source', async () => {
    cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts' }))

    render(<Harness />)

    const button = await screen.findByRole('button', { name: /^Revert All$/ })
    expect(button).toHaveTextContent('Revert All')
    expect(button).not.toHaveTextContent('↺')
  })
})

describe('DiffViewer split mode', () => {
  it('renders paired remove/add rows and unchanged dividers', () => {
    render(
      <LocaleProvider>
        <DiffViewer diff={makeDiff({ filePath: 'notes.txt' })} workspacePath="F:\\work" mode="split" />
      </LocaleProvider>
    )

    expect(screen.getByTestId('split-diff-body')).toBeInTheDocument()
    expect(screen.getAllByText('2 unchanged lines')).toHaveLength(2)
    expect(screen.getByText('old line')).toBeInTheDocument()
    expect(screen.getByText('new line')).toBeInTheDocument()
    expect(screen.getAllByText('shared line')).toHaveLength(2)
  })

  it('keeps long split diff lines inside independent synchronized panes', () => {
    const longLine = '<td width="25%" align="center"><b>很长很长的 Markdown 表格内容 with English text and symbols that should not bleed into the other pane</b></td>'
    render(
      <LocaleProvider>
        <DiffViewer
          diff={makeDiff({
            filePath: 'README.md',
            diffHunks: [
              {
                oldStart: 5,
                oldLines: 2,
                newStart: 5,
                newLines: 2,
                lines: [
                  { type: 'remove', content: longLine },
                  { type: 'add', content: `${longLine} updated` }
                ]
              }
            ]
          })}
          workspacePath="F:\\work"
          mode="split"
        />
      </LocaleProvider>
    )

    const leftPane = screen.getByTestId('split-left-pane')
    const rightPane = screen.getByTestId('split-right-pane')

    expect(leftPane).toHaveStyle({ overflowX: 'auto' })
    expect(rightPane).toHaveStyle({ overflowX: 'auto' })

    leftPane.scrollLeft = 88
    fireEvent.scroll(leftPane)
    expect(rightPane.scrollLeft).toBe(88)

    rightPane.scrollLeft = 33
    fireEvent.scroll(rightPane)
    expect(leftPane.scrollLeft).toBe(33)
  })

  it('renders plaintext safely without injecting HTML', () => {
    render(
      <LocaleProvider>
        <DiffViewer
          diff={makeDiff({
            filePath: 'notes.txt',
            diffHunks: [
              {
                oldStart: 1,
                oldLines: 1,
                newStart: 1,
                newLines: 1,
                lines: [
                  { type: 'add', content: '<img src=x onerror=alert(1)> plain text' }
                ]
              }
            ]
          })}
          workspacePath="F:\\work"
          mode="split"
        />
      </LocaleProvider>
    )

    expect(screen.getByText('<img src=x onerror=alert(1)> plain text')).toBeInTheDocument()
    expect(document.querySelector('img')).toBeNull()
  })
})
