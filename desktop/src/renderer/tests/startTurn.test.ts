import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { reissueFailedTurn, startTurnWithOptimisticUI } from '../utils/startTurn'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { WorkspaceProjectSummary } from '../../shared/workspaceProjects'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { installDesktopApiMock } from './desktopApiMock'

const sendRequest = vi.fn()
const image = { tempPath: 'C:\\temp\\image.png', dataUrl: 'data:image/png;base64,abc', fileName: 'image.png', mimeType: 'image/png' }
const file = { path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }

afterEach(() => useWorkspaceProjectsStore.getState().reset())

describe('startTurnWithOptimisticUI thread naming', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('window', {})
    installDesktopApiMock({ appServer: { sendRequest } })
    sendRequest.mockResolvedValue({ turn: { id: 'turn-1' } })
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
  })

  it.each([
    ['Image message', '', { images: [image] }],
    ['File reference message', '', { files: [file] }],
    ['Attachment message', '', { images: [image], files: [file] }],
    ['Review this change set', 'Review this change set', { files: [file] }]
  ])('names the thread %j', async (expected, text, attachments) => {
    useThreadStore.setState({
      threadList: [{
        id: 'thread-1',
        displayName: null,
        status: 'active',
        originChannel: 'dotcraft-desktop',
        createdAt: new Date().toISOString(),
        lastActiveAt: new Date().toISOString()
      }]
    })

    await startTurnWithOptimisticUI({
      threadId: 'thread-1',
      workspacePath: 'X:\\fixtures\\workspace',
      text,
      ...attachments,
      fallbackThreadName: 'Image message',
      fileFallbackThreadName: 'File reference message',
      attachmentFallbackThreadName: 'Attachment message'
    })

    expect(useThreadStore.getState().threadList[0]?.displayName).toBe(expected)
  })

  it('names an untitled thread from the user text before the turn starts', async () => {
    useThreadStore.setState({
      threadList: [{
        id: 'thread-1',
        displayName: null,
        status: 'active',
        originChannel: 'dotcraft-desktop',
        createdAt: new Date().toISOString(),
        lastActiveAt: new Date().toISOString()
      }]
    })

    await startTurnWithOptimisticUI({
      threadId: 'thread-1',
      workspacePath: 'X:\\fixtures\\workspace',
      text: 'Tighten the retry loop',
      contexts: [{ id: 'c1', kind: 'diffAnnotation', path: 'a.ts', side: 'right', startLine: 1, endLine: 1, comment: 'here' }],
      fallbackThreadName: 'Image message'
    })

    const methods = sendRequest.mock.calls.map(([method]) => method)
    expect(methods.indexOf('thread/rename')).toBeLessThan(methods.indexOf('turn/start'))
    expect(sendRequest).toHaveBeenCalledWith('thread/rename', { threadId: 'thread-1', displayName: 'Tighten the retry loop' })
    expect(useThreadStore.getState().threadList[0]?.displayName).toBe('Tighten the retry loop')
  })
})

describe('startTurnWithOptimisticUI echo notification', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('window', {})
    installDesktopApiMock({ appServer: { sendRequest } })
    sendRequest.mockResolvedValue({ turn: { id: 'turn-1' } })
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
  })

  const input = {
    threadId: 'thread-1',
    workspacePath: 'X:\\fixtures\\workspace',
    text: 'Continue the work',
    fallbackThreadName: 'Message',
    renameThreadFromText: false
  }

  it('notifies after the local message exists and before the RPC resolves, without notifying on promotion', async () => {
    let resolveStart!: (result: { turn: { id: string } }) => void
    sendRequest.mockImplementation(() => new Promise((resolve) => { resolveStart = resolve }))
    const onOptimisticEcho = vi.fn(() => {
      expect(useConversationStore.getState().turns).toMatchObject([{
        threadId: input.threadId,
        items: [{ type: 'userMessage', text: input.text, clientUserMessageId: 'message-1' }]
      }])
    })

    const pending = startTurnWithOptimisticUI({
      ...input,
      clientUserMessageId: 'message-1',
      onOptimisticEcho
    })

    expect(onOptimisticEcho).toHaveBeenCalledTimes(1)
    expect(sendRequest).toHaveBeenCalledWith('turn/start', expect.objectContaining({ threadId: input.threadId }))

    resolveStart({ turn: { id: 'turn-1' } })
    await pending

    expect(useConversationStore.getState().activeTurnId).toBe('turn-1')
    expect(onOptimisticEcho).toHaveBeenCalledTimes(1)
  })

  it('does not notify or submit when input parts are empty', async () => {
    const onOptimisticEcho = vi.fn()

    await startTurnWithOptimisticUI({ ...input, text: '', onOptimisticEcho })

    expect(onOptimisticEcho).not.toHaveBeenCalled()
    expect(sendRequest).not.toHaveBeenCalled()
    expect(useConversationStore.getState().turns).toEqual([])
  })

  it('submits without notifying when the turn is off screen', async () => {
    const onOptimisticEcho = vi.fn()

    await startTurnWithOptimisticUI({ ...input, onScreen: false, onOptimisticEcho })

    expect(sendRequest).toHaveBeenCalledWith('turn/start', expect.objectContaining({ threadId: input.threadId }))
    expect(onOptimisticEcho).not.toHaveBeenCalled()
    expect(useConversationStore.getState().turns).toEqual([])
  })
})

describe('Project workspace submission', () => {
  const input = {
    threadId: 'thread-1', workspacePath: 'X:/worktree', identityWorkspacePath: 'X:/app',
    text: 'Continue', fallbackThreadName: 'Message', renameThreadFromText: false
  }
  const project = (path: string, secondaryFolders: string[] = []): WorkspaceProjectSummary => ({
    kind: 'local', path, name: path, state: 'foreground', running: true, loaded: true,
    threadCount: 0, threads: [], pinned: false, secondaryFolders
  })

  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('window', {})
    installDesktopApiMock({ appServer: { sendRequest } })
    sendRequest.mockResolvedValue({ turn: { id: 'turn-1' } })
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
  })

  it.each([false, true])('submits only the owning project roots regardless of project order (reversed=%s)', async (reversed) => {
    const projects = [project('X:/other', ['X:/app']), project('X:/app', ['X:/private', 'X:/shared'])]
    useWorkspaceProjectsStore.setState({ projects: reversed ? projects.reverse() : projects })

    await startTurnWithOptimisticUI(input)

    const params = sendRequest.mock.calls.find(([method]) => method === 'turn/start')![1]
    expect(params.runtimeWorkspaceRoots).toEqual(['X:/app', 'X:/private', 'X:/shared'])
    expect(params).not.toHaveProperty('cwd')
  })

  it('replaces roots on the next turn and explicitly submits a single root on retry', async () => {
    useWorkspaceProjectsStore.setState({ projects: [project('X:/app', ['X:/shared'])] })
    await startTurnWithOptimisticUI(input)
    expect(sendRequest).toHaveBeenLastCalledWith('turn/start', expect.objectContaining({
      runtimeWorkspaceRoots: ['X:/app', 'X:/shared']
    }))

    useWorkspaceProjectsStore.setState({ projects: [project('X:/app', ['X:/shared', 'X:/private'])] })
    await startTurnWithOptimisticUI(input)
    expect(sendRequest).toHaveBeenLastCalledWith('turn/start', expect.objectContaining({
      runtimeWorkspaceRoots: ['X:/app', 'X:/shared', 'X:/private']
    }))

    useWorkspaceProjectsStore.setState({ projects: [project('X:/app')] })
    await reissueFailedTurn(input)
    expect(sendRequest).toHaveBeenLastCalledWith('turn/start', {
      threadId: 'thread-1', input: [], runtimeWorkspaceRoots: ['X:/app']
    })
  })

  it('keeps the owning identity when the primary is promoted and the folder list changes again', async () => {
    const promoted = { ...project('X:/app', ['X:/app', 'X:/shared']), primaryFolder: 'X:/lib' }
    useWorkspaceProjectsStore.setState({ projects: [promoted] })
    await startTurnWithOptimisticUI(input)
    expect(sendRequest).toHaveBeenLastCalledWith('turn/start', expect.objectContaining({
      runtimeWorkspaceRoots: ['X:/lib', 'X:/app', 'X:/shared']
    }))
    expect(sendRequest.mock.calls.at(-1)![1]).not.toHaveProperty('cwd')

    useWorkspaceProjectsStore.setState({ projects: [{ ...promoted, secondaryFolders: ['X:/app', 'X:/new-shared'] }] })
    await startTurnWithOptimisticUI(input)
    expect(sendRequest).toHaveBeenLastCalledWith('turn/start', expect.objectContaining({
      runtimeWorkspaceRoots: ['X:/lib', 'X:/app', 'X:/new-shared']
    }))
  })

  it.each([false, true])('projects nested cwd updates without losing the execution root (worktree=%s)', async (worktree) => {
    useThreadStore.setState({ activeThread: {
      id: 'thread-1', workspacePath: 'X:/app', effectiveWorkspacePath: worktree ? 'X:/worktree' : 'X:/lib/src',
      userId: 'local', metadata: {}, turns: [], displayName: null, status: 'active',
      originChannel: 'dotcraft-desktop', createdAt: '2026-11-06T00:00:00Z', lastActiveAt: '2026-11-06T00:00:00Z',
      configuration: { cwd: 'X:/lib/src', executionWorkspaceOverride: worktree ? 'X:/worktree' : undefined }
    } })
    const promoted = { ...project('X:/app'), primaryFolder: 'X:/lib' }
    useWorkspaceProjectsStore.setState({ projects: [promoted] })
    await startTurnWithOptimisticUI(input)
    const params = sendRequest.mock.calls.at(-1)![1]
    expect(params.runtimeWorkspaceRoots).toEqual(['X:/lib'])
    if (worktree) expect(params.cwd).toBe('X:/lib')
    else expect(params).not.toHaveProperty('cwd')

    useWorkspaceProjectsStore.setState({ projects: [{ ...promoted, primaryFolder: 'X:/new-primary' }] })
    await reissueFailedTurn(input)
    expect(sendRequest).toHaveBeenLastCalledWith('turn/start', {
      threadId: 'thread-1', input: [], runtimeWorkspaceRoots: ['X:/new-primary'], cwd: 'X:/new-primary'
    })
  })

  it.each(['unassigned', 'remote'] as const)('does not apply local folder membership to a %s workspace', async (kind) => {
    const projects = [project('X:/other', ['X:/app'])]
    if (kind !== 'unassigned') projects.push({ ...project('X:/app'), kind })
    useWorkspaceProjectsStore.setState({ projects })

    await startTurnWithOptimisticUI(input)

    const params = sendRequest.mock.calls.find(([method]) => method === 'turn/start')![1]
    expect(params).not.toHaveProperty('runtimeWorkspaceRoots')
  })
})
