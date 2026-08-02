import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ViewerTab } from '../components/detail/ViewerTab'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'

vi.mock('../components/detail/WorkspaceExplorer', () => ({
  WorkspaceExplorer: () => <div>Workspace explorer</div>
}))

describe('ViewerTab Files placeholder', () => {
  beforeEach(() => {
    useViewerTabStore.setState({
      byThread: new Map(),
      currentThreadId: null,
      currentWorkspacePath: null
    })
    useUIStore.setState({ explorerVisible: true })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) }
      }
    })
  })

  it('uses the same open-folder explorer icon as populated file viewers', () => {
    const tabId = useViewerTabStore.getState().openFiles({
      threadId: 'thread-1',
      initialLabel: 'Open file'
    })
    useViewerTabStore.getState().onThreadSwitched('thread-1')

    render(
      <LocaleProvider>
        <ViewerTab tabId={tabId} />
      </LocaleProvider>
    )

    const explorerToggle = screen.getByRole('button', { name: 'Hide explorer' })
    expect(explorerToggle.querySelector('.lucide-folder-open')).not.toBeNull()
    expect(explorerToggle).toHaveAttribute('data-active-tone', 'neutral')
  })
})
