// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DiffAnnotationContext, FileAnnotationContext } from '../../shared/composerContext'
import type { FileViewerTab } from '../../shared/viewer/types'
import { ContextFeedback } from '../components/conversation/ContextFeedback'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConversationStore } from '../stores/conversationStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { installDesktopApiMock } from './desktopApiMock'

const THREAD = 'thread-1'
const WORKSPACE = '/workspace'

const annotation: FileAnnotationContext = {
  id: 'cells',
  kind: 'fileAnnotation',
  path: `${WORKSPACE}/model.xlsx`,
  target: 'Revenue!B6:D8',
  targetKind: 'range',
  location: { sheet: 'Revenue', range: 'B6:D8', rowCount: 3, columnCount: 3 },
  comment: 'check totals'
}

const lineComment: DiffAnnotationContext = {
  id: 'line',
  kind: 'diffAnnotation',
  path: `${WORKSPACE}/src/app.ts`,
  side: 'right',
  startLine: 3,
  endLine: 3,
  comment: 'why?'
}

const authorizeFile = vi.fn(async ({ absolutePath }: { absolutePath: string }) => ({ absolutePath }))
const onRemove = vi.fn()

beforeEach(() => {
  authorizeFile.mockClear()
  onRemove.mockClear()
  installDesktopApiMock({
    workspace: {
      viewer: {
        authorizeFile,
        classify: async () => ({ contentClass: 'spreadsheet' as const, mime: '', sizeBytes: 10, mtimeMs: 1 })
      }
    }
  })
  useConversationStore.setState({ workspacePath: WORKSPACE, remoteWorkspaceActive: false })
  useViewerTabStore.setState({ byThread: new Map(), currentThreadId: THREAD, currentWorkspacePath: WORKSPACE })
})

describe('file annotation comments in the composer', () => {
  it('groups with line comments and reopens the file at its target', async () => {
    render(
      <LocaleProvider loadSettings={false}>
        <ContextFeedback contexts={[lineComment, annotation]} onRemove={onRemove} />
      </LocaleProvider>
    )
    expect(screen.getAllByRole('button', { expanded: false })).toHaveLength(1)

    fireEvent.click(screen.getByRole('button', { expanded: false }))
    fireEvent.click(await screen.findByRole('button', { name: /model\.xlsx/ }))

    await waitFor(() => expect(authorizeFile).toHaveBeenCalledWith({ absolutePath: annotation.path }))
    await waitFor(() => {
      const tab = useViewerTabStore.getState().getThreadState(THREAD).tabs[0] as FileViewerTab | undefined
      expect(tab).toMatchObject({
        absolutePath: annotation.path,
        contentClass: 'spreadsheet',
        pendingReveal: { targetKind: 'range', location: annotation.location }
      })
    })
  })

  it('removes file annotations together with line comments', () => {
    render(
      <LocaleProvider loadSettings={false}>
        <ContextFeedback contexts={[lineComment, annotation]} onRemove={onRemove} />
      </LocaleProvider>
    )
    const remove = screen.getAllByRole('button').find((button) => !button.hasAttribute('aria-expanded'))!
    fireEvent.click(remove)
    expect(onRemove.mock.calls.map(([id]) => id)).toEqual(['line', 'cells'])
  })
})
