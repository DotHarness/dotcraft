// @vitest-environment jsdom
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { translate } from '../../shared/locales'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ChangesTab } from '../components/detail/ChangesTab'
import { DiffViewer } from '../components/detail/DiffViewer'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import type { ConversationTurn } from '../types/conversation'
import type { FileDiff } from '../types/toolCall'
import type { TurnDiff, TurnFileChange } from '../types/turnDiff'
import { installDesktopApiMock } from './desktopApiMock'

/**
 * The word-level diff draws a changed line as several runs, so the row carries the
 * whole line rather than a single text node.
 */
function diffRows(text: string): HTMLElement[] {
  return [...document.querySelectorAll<HTMLElement>('[data-line]')]
    .filter((row) => row.textContent === text)
}

const cs = () => useConversationStore.getState()
const ui = () => useUIStore.getState()

function makeDiff(overrides: Partial<FileDiff> = {}): FileDiff {
  return {
    filePath: 'src/a.ts',
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

function seedRow(overrides: Partial<TurnFileChange> = {}): void {
  const row: TurnFileChange = {
    key: 'turn-1::item-1',
    turnId: 'turn-1',
    diff: makeDiff(),
    patchText: 'diff --git a/src/a.ts b/src/a.ts',
    truncated: false,
    ...overrides
  }
  useConversationStore.setState({
    turnDiffs: new Map([['turn-1', { turnId: 'turn-1', source: 'history', files: [row] }]])
  })
}

function fileRow(turnId: string, name: string, overrides: Partial<TurnFileChange> = {}): TurnFileChange {
  return {
    key: `${turnId}::${name}`,
    turnId,
    diff: makeDiff({ filePath: `src/${name}.ts` }),
    patchText: `diff --git a/src/${name}.ts b/src/${name}.ts`,
    truncated: false,
    ...overrides
  }
}

function seedTurns(...turns: Array<[turnId: string, rows: TurnFileChange[]]>): void {
  useConversationStore.setState({
    turns: turns.map(([id], index): ConversationTurn => ({
      id,
      threadId: 'thread-1',
      status: 'completed',
      items: [],
      startedAt: new Date(Date.UTC(2026, 8, 24, 10, index)).toISOString()
    })),
    turnDiffs: new Map(turns.map(([turnId, files]): [string, TurnDiff] => [turnId, { turnId, source: 'history', files }]))
  })
}

const changeKeys = (): Array<string | undefined> =>
  [...document.querySelectorAll<HTMLElement>('[data-change-key]')].map((row) => row.dataset.changeKey)

function Harness({ workspacePath = 'F:\\work' }: { workspacePath?: string }): JSX.Element {
  return (
    <LocaleProvider>
      <ChangesTab workspacePath={workspacePath} />
    </LocaleProvider>
  )
}

const writeText = vi.fn()

beforeEach(() => {
  cs().reset()
  useThreadStore.setState({ activeThreadId: 'thread-1' })
  useUIStore.setState({
    selectedChangeKey: null,
    changesDiffModeByThread: {},
    explorerVisible: false,
    activeDetailTab: { kind: 'system', id: 'changes' },
    detailPanelVisible: true,
    detailPanelPreferredVisible: true
  })
  writeText.mockReset().mockResolvedValue(undefined)
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } })
  installDesktopApiMock({
      settings: {
        get: async () => ({ locale: 'en' })
      },
      shell: {
        launchEditor: vi.fn(async () => {}),
        showItemInFolder: vi.fn(async () => {})
      }
    })
})

describe('ChangesTab', () => {
  it('stores split diff mode per thread', async () => {
    seedRow()

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

  it('reveals the changed file in the OS file manager from the hover action', async () => {
    seedRow()

    render(<Harness workspacePath={'F:\\work'} />)

    fireEvent.click(screen.getByLabelText('Open containing folder'))

    await waitFor(() => {
      expect(window.api.shell.showItemInFolder).toHaveBeenCalledWith('F:\\work\\src\\a.ts')
    })
  })

  it('shows only the newest turn that changed files, without revert controls', async () => {
    seedTurns(
      ['turn-1', [fileRow('turn-1', 'a'), fileRow('turn-1', 'b')]],
      ['turn-2', [fileRow('turn-2', 'c'), fileRow('turn-2', 'd')]]
    )

    await act(async () => { render(<Harness />) })

    expect(changeKeys()).toEqual(['turn-2::c', 'turn-2::d'])
    expect(screen.queryByRole('button', { name: /revert|undo|re-?apply/i })).toBeNull()
  })

  it('copies the workspace-relative path from the hover action', async () => {
    seedRow({ diff: makeDiff({ filePath: 'F:\\work\\src\\a.ts' }) })

    render(<Harness />)
    fireEvent.click(screen.getByRole('button', { name: translate('en', 'changesFile.copyPath') }))

    await waitFor(() => expect(writeText).toHaveBeenCalledWith('src/a.ts'))
  })

  it('explains a truncated row instead of rendering its diff', async () => {
    seedRow({ truncated: true })

    await act(async () => { render(<Harness />) })

    expect(screen.getByRole('button', { expanded: true })).toBeInTheDocument()
    expect(screen.getByText(translate('en', 'changes.truncated'))).toBeInTheDocument()
    expect(diffRows('new line')).toEqual([])
  })

  it('lists the same rows in the explorer dock and selects the clicked one', async () => {
    useUIStore.setState({ explorerVisible: true })
    seedTurns(
      ['turn-1', [fileRow('turn-1', 'a')]],
      ['turn-2', [fileRow('turn-2', 'c'), fileRow('turn-2', 'd')]]
    )

    await act(async () => { render(<Harness />) })

    const items = within(screen.getByRole('list', { name: translate('en', 'changes.fileListTitle') })).getAllByRole('listitem')
    expect(items).toHaveLength(changeKeys().length)
    fireEvent.click(items[1])
    expect(ui().selectedChangeKey).toBe('turn-2::d')
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
    expect(diffRows('old line')).toHaveLength(1)
    expect(diffRows('new line')).toHaveLength(1)
    expect(diffRows('shared line')).toHaveLength(2)
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
