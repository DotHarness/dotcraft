import { describe, expect, it, vi, beforeEach } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { UserMessageBlock } from '../components/conversation/UserMessageBlock'
import { useConversationStore } from '../stores/conversationStore'
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
const shellListEditors = vi.fn()
const shellLaunchLocalPathInEditor = vi.fn()
const shellOpenLocalPath = vi.fn()
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
    shellListEditors.mockResolvedValue([
      { id: 'explorer', labelKey: 'editors.explorer', iconKey: 'explorer' },
      { id: 'cursor', labelKey: 'editors.cursor', iconKey: 'editor-generic' }
    ])
    shellLaunchLocalPathInEditor.mockResolvedValue(undefined)
    shellOpenLocalPath.mockResolvedValue(undefined)
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
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
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
          listEditors: shellListEditors,
          launchLocalPathInEditor: shellLaunchLocalPathInEditor,
          openLocalPath: shellOpenLocalPath,
          revealLocalPath: shellRevealLocalPath
        }
      }
    })
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: clipboardWriteText }
    })
  })

  it('renders goal continuation user messages with a goal source pill', async () => {
    renderWithLocale(
      <UserMessageBlock
        text="Continue working toward the active thread goal"
        triggerKind="goal"
        triggerLabel="Goal continuation"
        triggerRefId="goal-1"
      />
    )

    const pill = screen.getByText('Goal auto-continue')
    expect(pill).toBeInTheDocument()
    fireEvent.mouseEnter(pill.parentElement?.parentElement as HTMLElement)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Goal auto-continue · Goal continuation')
  })

  it('renders user-authored goal submissions with a distinct goal sent pill', () => {
    renderWithLocale(
      <UserMessageBlock
        text="Build feature"
        sentAsGoal
      />
    )

    expect(screen.getByText('Sent as goal')).toBeInTheDocument()
    expect(screen.queryByText('Goal auto-continue')).toBeNull()
  })

  it('localizes user-authored goal submission pills in zh-Hans', async () => {
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

  it('keeps automation source pills visible for automation triggers', () => {
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

  it('renders team-triggered native display text without runtime envelope tags', () => {
    renderWithLocale(
      <UserMessageBlock
        text='<team-notification type="mission.finalize">hidden model envelope</team-notification>'
        nativeInputParts={[
          { type: 'text', text: 'Mission ready for Leader finalization: Ship Teams' }
        ]}
        triggerKind="team"
        triggerLabel="Finalize mission: Ship Teams"
        triggerRefId="mission-1"
      />
    )

    expect(screen.getByText('Mission ready for Leader finalization: Ship Teams')).toBeInTheDocument()
    expect(screen.queryByText(/team-notification/)).toBeNull()
    expect(screen.getByRole('button', { name: 'Sent by Teams · Teams: Finalize mission: Ship Teams' })).toBeInTheDocument()
  })

  it('renders SubAgent follow-up source pills with thread-style copy', async () => {
    renderWithLocale(
      <UserMessageBlock
        text="Continue work"
        triggerKind="subagentFollowupTask"
        triggerLabel="Inspect"
        triggerRefId="/root/inspect"
      />
    )

    const pill = screen.getByText('Sent by DotCraft from another thread')
    expect(pill).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Sent by DotCraft from another thread · Follow-up task · Inspect' })).toBeNull()
    fireEvent.mouseEnter(pill.parentElement?.parentElement as HTMLElement)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Sent by DotCraft from another thread · Follow-up task · Inspect')
  })

  it('renders SubAgent mailbox source detail', async () => {
    renderWithLocale(
      <UserMessageBlock
        text="Mailbox note"
        triggerKind="subagentMailbox"
        triggerLabel="/root/review"
        triggerRefId="/root/inspect"
      />
    )

    const pill = screen.getByText('Sent by DotCraft from another thread')
    fireEvent.mouseEnter(pill.parentElement?.parentElement as HTMLElement)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Sent by DotCraft from another thread · Mailbox message · /root/review')
  })

  it('localizes SubAgent source pills in zh-Hans', async () => {
    settingsGet.mockResolvedValue({ locale: 'zh-Hans' })

    renderWithLocale(
      <UserMessageBlock
        text="继续"
        triggerKind="subagentInput"
        triggerLabel="Inspect"
        triggerRefId="/root/inspect"
      />
    )

    const pill = await screen.findByText('DotCraft 从另一个会话发送')
    expect(pill).toBeInTheDocument()
    fireEvent.mouseEnter(pill.parentElement?.parentElement as HTMLElement)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('DotCraft 从另一个会话发送 · 直接输入 · Inspect')
  })

  it('renders guidance user messages with a steered conversation marker', () => {
    renderWithLocale(
      <UserMessageBlock
        text="Focus on failing tests first"
        deliveryMode="guidance"
      />
    )

    expect(screen.getByText('Steered conversation')).toBeInTheDocument()
  })

  it('does not render the steered conversation marker for normal or queued user messages', () => {
    const { rerender } = render(
      <LocaleProvider>
        <UserMessageBlock text="Normal message" />
      </LocaleProvider>
    )

    expect(screen.queryByText('Steered conversation')).toBeNull()

    rerender(
      <LocaleProvider>
        <UserMessageBlock text="Queued message" deliveryMode="queued" />
      </LocaleProvider>
    )

    expect(screen.queryByText('Steered conversation')).toBeNull()
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
          available: true,
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
      expect(appServerSendRequest).toHaveBeenCalledWith('skills/list', { includeUnavailable: true })
      expect(clipboardWriteText).toHaveBeenCalledWith('C:\\Users\\tester\\.craft\\skills\\memory\\SKILL.md')
    })
  })

  it('supports editor, default app, and Explorer actions from the file chip context menu', async () => {
    settingsGet.mockResolvedValue({ locale: 'en', lastOpenEditorId: 'cursor' })

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
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Open in Cursor' }))

    await waitFor(() => {
      expect(shellLaunchLocalPathInEditor).toHaveBeenCalledWith('cursor', 'F:/workspace/src/App.tsx')
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
      expect(shellOpenLocalPath).toHaveBeenCalledWith('F:/workspace/src/App.tsx')
    })
  })

  it('uses the current File Explorer opener as the primary context menu action', async () => {
    settingsGet.mockResolvedValue({ locale: 'en', lastOpenEditorId: 'explorer' })

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
      expect(shellRevealLocalPath).toHaveBeenCalledWith('F:/workspace/src/App.tsx')
      expect(shellOpenLocalPath).not.toHaveBeenCalled()
      expect(settingsSet).toHaveBeenCalledWith({ lastOpenEditorId: 'explorer' })
    })
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
