import { describe, expect, it, vi, beforeEach } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { UserMessageBlock } from '../components/conversation/UserMessageBlock'
import { useConversationStore } from '../stores/conversationStore'
import { useOpenTargetsStore } from '../stores/openTargetsStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { useSkillsStore } from '../stores/skillsStore'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const readImageAsDataUrl = vi.fn()
const authorizeFile = vi.fn()
const classify = vi.fn()
const shellOpenExternal = vi.fn()
const shellListOpenTargets = vi.fn()
const shellOpenInTarget = vi.fn()
const shellRevealLocalPath = vi.fn()
const appServerSendRequest = vi.fn()
const clipboardWriteText = vi.fn()

function renderWithLocale(ui: JSX.Element): void {
  render(
    <LocaleProvider>
      {ui}
    </LocaleProvider>
  )
}

describe('UserMessageBlock trigger source pills', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    settingsSet.mockResolvedValue(undefined)
    readImageAsDataUrl.mockResolvedValue({ dataUrl: '' })
    authorizeFile.mockImplementation(async ({ absolutePath }: { absolutePath: string }) => ({ absolutePath }))
    classify.mockResolvedValue({ contentClass: 'text', mime: 'text/plain', sizeBytes: 10 })
    useOpenTargetsStore.setState({ status: 'idle', targets: [], preferences: undefined })
    shellListOpenTargets.mockResolvedValue([
      { id: 'cursor', label: 'Cursor', iconKey: 'editor' },
      { id: 'fileManager', label: 'File Explorer', labelKey: 'openTargets.fileManager', iconKey: 'fileManager' },
      { id: 'systemDefault', label: 'Default app', labelKey: 'conversation.reference.defaultApp', iconKey: 'fileManager', hidden: true }
    ])
    shellOpenInTarget.mockResolvedValue(undefined)
    shellRevealLocalPath.mockResolvedValue(undefined)
    appServerSendRequest.mockResolvedValue({ skills: [] })
    clipboardWriteText.mockResolvedValue(undefined)
    useConversationStore.getState().reset()
    useConversationStore.setState({ workspacePath: 'F:/workspace' })
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    useSkillsStore.setState({
      skills: [],
      loading: false,
      error: null,
      selectedSkillName: null,
      skillContent: null,
      contentLoading: false
    })
    useViewerTabStore.setState({
      byThread: new Map(),
      currentThreadId: 'thread-1',
      currentWorkspacePath: 'F:/workspace'
    })
    useUIStore.setState({
      activeDetailTab: { kind: 'system', id: 'changes' },
      detailPanelPreferredVisible: false,
      detailPanelVisible: false
    })
    installDesktopApiMock({
      settings: { get: settingsGet, set: settingsSet },
      appServer: { sendRequest: appServerSendRequest },
      workspace: {
        readImageAsDataUrl,
        viewer: {
          authorizeFile,
          classify
        }
      },
      shell: {
        openExternal: shellOpenExternal,
        listOpenTargets: shellListOpenTargets,
        openInTarget: shellOpenInTarget,
        revealLocalPath: shellRevealLocalPath
      }
    })
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: clipboardWriteText }
    })
  })

  it('renders a goal continuation origin line whose tooltip omits the echoed label', async () => {
    renderWithLocale(
      <UserMessageBlock
        text="Continue working toward the active thread goal"
        triggerKind="goal"
        triggerLabel="Goal continuation"
        triggerRefId="goal-1"
      />
    )

    const line = screen.getByText('Goal auto-continue')
    expect(line).toBeInTheDocument()
    fireEvent.mouseEnter(line.parentElement?.parentElement as HTMLElement)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Goal continuation')
    expect(await screen.findByRole('tooltip')).not.toHaveTextContent('Goal auto-continue')
  })

  it('omits the tooltip when a trigger arrives without a label', () => {
    renderWithLocale(
      <UserMessageBlock
        text="Continue"
        triggerKind="thread"
        triggerRefId="thread-1"
      />
    )

    const line = screen.getByText('Sent by DotCraft from another thread')
    fireEvent.mouseEnter(line.parentElement?.parentElement as HTMLElement)
    expect(screen.queryByRole('tooltip')).toBeNull()
  })

  it('keeps the full origin sentence as the accessible name of a navigable line', () => {
    renderWithLocale(
      <UserMessageBlock
        text="Continue"
        triggerKind="thread"
        triggerLabel="Review workspace onboarding"
        triggerRefId="thread-1"
      />
    )

    expect(
      screen.getByRole('button', {
        name: 'Sent by DotCraft from another thread · Started from Review workspace onboarding'
      })
    ).toBeInTheDocument()
  })

  it('renders user-authored goal submissions as a state marker', () => {
    renderWithLocale(
      <UserMessageBlock
        text="Build feature"
        sentAsGoal
      />
    )

    expect(screen.getByText('Sent as goal')).toBeInTheDocument()
    expect(screen.queryByText('Goal auto-continue')).toBeNull()
  })

  it('shows the goal state alongside an origin, since they occupy different zones', () => {
    renderWithLocale(
      <UserMessageBlock
        text="Build feature"
        triggerKind="automation"
        triggerLabel="Nightly checks"
        triggerRefId="task-1"
        sentAsGoal
      />
    )

    expect(screen.getByText('Sent as goal')).toBeInTheDocument()
    expect(screen.getByText('Sent via automation')).toBeInTheDocument()
  })

  it('localizes user-authored goal submission markers in zh-Hans', async () => {
    settingsGet.mockResolvedValue({ locale: 'zh-Hans' })

    renderWithLocale(
      <UserMessageBlock
        text="构建功能"
        sentAsGoal
      />
    )

    expect(await screen.findByText('设为目标')).toBeInTheDocument()
    expect(screen.queryByText('目标自动推进')).toBeNull()
  })

  it('keeps automation origins navigable', () => {
    renderWithLocale(
      <UserMessageBlock
        text="Run scheduled maintenance"
        triggerKind="automation"
        triggerLabel="Nightly checks"
        triggerRefId="task-1"
      />
    )

    expect(screen.getByRole('button', { name: 'Sent via automation · Automation · Nightly checks' })).toBeInTheDocument()
  })

  it('opens the originating workflow from its source marker', () => {
    renderWithLocale(
      <UserMessageBlock
        text="Workflow completed."
        triggerKind="workflow"
        triggerLabel="release-review"
        triggerRefId="run-1"
      />
    )

    fireEvent.click(screen.getByRole('button', { name: 'Sent from workflow · Workflow · release-review' }))
    const state = useViewerTabStore.getState().getThreadState('thread-1')
    expect(state.tabs).toEqual([
      expect.objectContaining({ kind: 'workflow', runId: 'run-1', threadId: 'thread-1' })
    ])
    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'viewer', id: state.tabs[0].id })
  })

  it('renders SubAgent follow-up origins with thread-style copy', async () => {
    renderWithLocale(
      <UserMessageBlock
        text="Continue work"
        triggerKind="subagentFollowupTask"
        triggerLabel="Inspect"
        triggerRefId="/root/inspect"
      />
    )

    const line = screen.getByText('Sent by DotCraft from another thread')
    expect(line).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Sent by DotCraft from another thread/ })).toBeNull()
    fireEvent.mouseEnter(line.parentElement?.parentElement as HTMLElement)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Follow-up task · Inspect')
  })

  it('renders SubAgent mailbox origin detail', async () => {
    renderWithLocale(
      <UserMessageBlock
        text="Mailbox note"
        triggerKind="subagentMailbox"
        triggerLabel="/root/review"
        triggerRefId="/root/inspect"
      />
    )

    const line = screen.getByText('Sent by DotCraft from another thread')
    fireEvent.mouseEnter(line.parentElement?.parentElement as HTMLElement)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Mailbox message · /root/review')
  })

  it('localizes SubAgent origin lines in zh-Hans', async () => {
    settingsGet.mockResolvedValue({ locale: 'zh-Hans' })

    renderWithLocale(
      <UserMessageBlock
        text="继续"
        triggerKind="subagentInput"
        triggerLabel="Inspect"
        triggerRefId="/root/inspect"
      />
    )

    const line = await screen.findByText('DotCraft 从另一个会话发送')
    expect(line).toBeInTheDocument()
    fireEvent.mouseEnter(line.parentElement?.parentElement as HTMLElement)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('直接输入 · Inspect')
  })

  it('opens workspace-external file chips in the internal viewer', async () => {
    renderWithLocale(
      <UserMessageBlock
        text=""
        nativeInputParts={[
          { type: 'fileRef', path: 'C:\\temp\\notes.txt', displayPath: 'notes.txt' }
        ]}
      />
    )

    fireEvent.click(screen.getByRole('button', { name: 'Open notes.txt in DotCraft viewer' }))

    await waitFor(() => {
      expect(authorizeFile).toHaveBeenCalledWith({ absolutePath: 'C:/temp/notes.txt' })
      expect(classify).toHaveBeenCalledWith({ absolutePath: 'C:/temp/notes.txt' })
    })
    const activeTab = useUIStore.getState().activeDetailTab
    expect(activeTab.kind).toBe('viewer')
    if (activeTab.kind === 'viewer') {
      const tab = useViewerTabStore.getState().getThreadState('thread-1').tabs.find((entry) => entry.id === activeTab.id)
      expect(tab).toMatchObject({
        kind: 'file',
        absolutePath: 'C:/temp/notes.txt',
        relativePath: 'C:/temp/notes.txt',
        contentClass: 'text'
      })
    }
  })

  it('opens a Markdown file link chip at its linked position', async () => {
    renderWithLocale(<UserMessageBlock text="Check [the handler](src/App.tsx:12:3)" />)

    fireEvent.click(screen.getByRole('button', { name: 'Open App.tsx in DotCraft viewer' }))

    await waitFor(() => {
      expect(classify).toHaveBeenCalledWith({ absolutePath: 'F:/workspace/src/App.tsx' })
    })
    const activeTab = useUIStore.getState().activeDetailTab
    expect(activeTab.kind).toBe('viewer')
    if (activeTab.kind === 'viewer') {
      const tab = useViewerTabStore.getState().getThreadState('thread-1').tabs.find((entry) => entry.id === activeTab.id)
      expect(tab).toMatchObject({
        kind: 'file',
        absolutePath: 'F:/workspace/src/App.tsx',
        navigationHint: { line: 12, column: 3 }
      })
    }
  })

  it('copies the resolved path from a user file chip context menu', async () => {
    renderWithLocale(
      <UserMessageBlock
        text=""
        nativeInputParts={[
          { type: 'fileRef', path: 'src/App.tsx', displayPath: 'src/App.tsx' }
        ]}
      />
    )

    fireEvent.contextMenu(screen.getByRole('button', { name: 'Open App.tsx in DotCraft viewer' }), {
      clientX: 12,
      clientY: 18
    })
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Copy path' }))

    await waitFor(() => {
      expect(clipboardWriteText).toHaveBeenCalledWith('F:/workspace/src/App.tsx')
    })
  })

  it('resolves a skill chip to SKILL.md before showing the path context menu', async () => {
    appServerSendRequest.mockResolvedValue({
      skills: [
        {
          name: 'memory',
          description: '',
          source: 'user',
          enabled: true,
          path: 'C:\\Users\\tester\\.craft\\skills\\memory\\SKILL.md'
        }
      ]
    })

    renderWithLocale(
      <UserMessageBlock
        text=""
        nativeInputParts={[{ type: 'skillRef', name: 'memory' }]}
      />
    )

    const skillChip = screen.getByText('memory').closest('span')
    expect(skillChip).not.toBeNull()
    fireEvent.contextMenu(skillChip!, { clientX: 20, clientY: 24 })
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Copy path' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('skills/list', {})
      expect(clipboardWriteText).toHaveBeenCalledWith('C:\\Users\\tester\\.craft\\skills\\memory\\SKILL.md')
    })
  })

  it('supports editor, default app, and Explorer actions from the file chip context menu', async () => {
    settingsGet.mockResolvedValue({ locale: 'en', openTargetPreferences: { global: 'fileManager' } })

    renderWithLocale(
      <UserMessageBlock
        text=""
        nativeInputParts={[
          { type: 'fileRef', path: 'src/App.tsx', displayPath: 'src/App.tsx' }
        ]}
      />
    )

    const chip = screen.getByRole('button', { name: 'Open App.tsx in DotCraft viewer' })
    fireEvent.contextMenu(chip, { clientX: 12, clientY: 18 })
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Open in File Explorer' }))

    await waitFor(() => {
      expect(shellOpenInTarget).toHaveBeenCalledWith({ targetId: 'fileManager', path: 'F:/workspace/src/App.tsx' })
    })

    fireEvent.contextMenu(chip, { clientX: 12, clientY: 18 })
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Open in Explorer' }))

    await waitFor(() => {
      expect(shellRevealLocalPath).toHaveBeenCalledWith('F:/workspace/src/App.tsx')
    })

    fireEvent.contextMenu(chip, { clientX: 12, clientY: 18 })
    fireEvent.mouseEnter(await screen.findByRole('menuitem', { name: 'Open with' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Default app' }))

    await waitFor(() => {
      expect(shellOpenInTarget).toHaveBeenCalledWith({ targetId: 'systemDefault', path: 'F:/workspace/src/App.tsx' })
    })
    expect(settingsSet).not.toHaveBeenCalled()
  })

  it('opens a file chip in another app without changing the default open target', async () => {
    settingsGet.mockResolvedValue({ locale: 'en', openTargetPreferences: { global: 'fileManager' } })

    renderWithLocale(
      <UserMessageBlock
        text=""
        nativeInputParts={[
          { type: 'fileRef', path: 'src/App.tsx', displayPath: 'src/App.tsx' }
        ]}
      />
    )

    const chip = screen.getByRole('button', { name: 'Open App.tsx in DotCraft viewer' })
    fireEvent.contextMenu(chip, { clientX: 12, clientY: 18 })
    fireEvent.mouseEnter(await screen.findByRole('menuitem', { name: 'Open with' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Cursor' }))

    await waitFor(() => {
      expect(shellOpenInTarget).toHaveBeenCalledWith({ targetId: 'cursor', path: 'F:/workspace/src/App.tsx' })
    })
    expect(settingsSet).not.toHaveBeenCalled()
  })

  it('previews successfully hydrated message images in the lightbox', async () => {
    readImageAsDataUrl.mockResolvedValue({ dataUrl: 'data:image/png;base64,AA==' })

    renderWithLocale(
      <UserMessageBlock
        text=""
        images={[{ path: 'F:/workspace/photo.png', fileName: 'photo.png', mimeType: 'image/png' }]}
      />
    )

    const button = await screen.findByRole('button', { name: 'View attached image 1' })
    fireEvent.click(button)

    expect(screen.getByRole('dialog', { name: 'Image preview' })).toBeInTheDocument()
    expect(authorizeFile).not.toHaveBeenCalled()
    expect(useViewerTabStore.getState().getThreadState('thread-1').tabs).toEqual([])
  })

  it('keeps failed external image rehydration clickable for the internal viewer', async () => {
    readImageAsDataUrl.mockRejectedValue(new Error('outside workspace'))
    authorizeFile.mockResolvedValue({ absolutePath: 'D:/pics/photo.png' })
    classify.mockResolvedValue({ contentClass: 'image', mime: 'image/png', sizeBytes: 20 })

    renderWithLocale(
      <UserMessageBlock
        text=""
        images={[{ path: 'D:/pics/photo.png', fileName: 'photo.png', mimeType: 'image/png' }]}
      />
    )

    const button = await screen.findByRole('button', { name: 'Open image photo.png in DotCraft viewer' })
    fireEvent.click(button)

    await waitFor(() => {
      expect(authorizeFile).toHaveBeenCalledWith({ absolutePath: 'D:/pics/photo.png' })
      expect(classify).toHaveBeenCalledWith({ absolutePath: 'D:/pics/photo.png' })
    })
    const activeTab = useUIStore.getState().activeDetailTab
    expect(activeTab.kind).toBe('viewer')
    if (activeTab.kind === 'viewer') {
      const tab = useViewerTabStore.getState().getThreadState('thread-1').tabs.find((entry) => entry.id === activeTab.id)
      expect(tab).toMatchObject({
        kind: 'file',
        absolutePath: 'D:/pics/photo.png',
        contentClass: 'image'
      })
    }
  })
})
