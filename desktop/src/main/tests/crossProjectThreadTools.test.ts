import { describe, expect, it, vi } from 'vitest'
import {
  DESKTOP_CONVERSATION_TOOL_NAMESPACE,
  DESKTOP_THREAD_TOOL_NAMESPACE,
  handleDesktopRuntimeThreadToolCall,
  threadTaskConnection,
  type AppServerRequestClient,
  type DesktopAppServerRequestOptions
} from '../desktopRuntimeThreadTools'
import { DesktopLinkedChats } from '../threadTasks/linkedChats'
import { DesktopThreadTasks } from '../threadTasks/threadTaskService'
import { ThreadTaskStore } from '../threadTasks/threadTaskStore'
import type { ThreadProjectLocation } from '../threadTasks/threadProjects'

const chats = { id: 'chats', name: 'Chats', path: '/workspace/chats', running: true }
const library = { id: 'library', name: 'Library', path: '/projects/library', running: false }
const parent = { threadId: 'parent', projectId: chats.id, workspacePath: chats.path }
const child = {
  threadId: 'child', projectId: library.id, workspacePath: library.path, projectName: library.name, title: 'Test library'
}

function fixture() {
  const source = vi.fn(async (_method: string, _params?: unknown): Promise<unknown> => {
    throw new Error('Expected a request on the target project')
  })
  const target = vi.fn(async (method: string, _params?: unknown): Promise<unknown> => {
    switch (method) {
      case 'thread/start':
      case 'worktree/createAndStart': return { thread: { id: child.threadId, displayName: child.title } }
      case 'thread/read': return { thread: { id: child.threadId, status: 'active', runtime: { activeTurnId: 'turn-1' } } }
      case 'thread/list': return { data: [{ id: child.threadId }], nextCursor: 'next', totalMatched: 2 }
      case 'thread/searchOccurrences': return { data: [{ occurrence: 'library' }] }
      case 'turn/start': return { turn: { id: 'turn-1' } }
      case 'turn/startOrEnqueue': return { queuedInput: { id: 'queued-1' } }
      case 'turn/interrupt': return {}
      default: throw new Error(`Unexpected request: ${method}`)
    }
  })
  const sourceClient = { sendRequest: source } as AppServerRequestClient
  const targetClient = { sendRequest: target } as AppServerRequestClient
  const connect = vi.fn(async (_project: ThreadProjectLocation) => threadTaskConnection(targetClient, library.path, {
    project: library, projectWorkspaceFor: () => ({ cwd: '/projects/library/src', runtimeWorkspaceRoots: ['/projects/library/src'] })
  }))
  const list = vi.fn(async () => [chats, library])
  const links = new DesktopLinkedChats(null)
  const tasks = new DesktopThreadTasks({ linkedChats: links, store: new ThreadTaskStore(null), getNewTaskPrompt: () => 'ask' })
  const options: DesktopAppServerRequestOptions = {
    project: chats, projects: { list, connect }, tasks
  }
  const call = (tool: string, args: Record<string, unknown>, overrides: DesktopAppServerRequestOptions = {}) =>
    handleDesktopRuntimeThreadToolCall(sourceClient, {
      namespace: tool === 'CreateThread' ? DESKTOP_CONVERSATION_TOOL_NAMESPACE : DESKTOP_THREAD_TOOL_NAMESPACE,
      threadId: parent.threadId,
      turnId: 'parent-turn',
      tool,
      arguments: args
    }, chats.path, { ...options, ...overrides })
  const composerTurn = () => tasks.handleNotification('turn/started', {
    turn: { id: 'parent-turn', threadId: parent.threadId, originChannel: 'dotcraft-desktop',
      items: [{ type: 'userMessage', payload: { text: 'Start a chat' } }] }
  })
  return { call, composerTurn, connect, list, source, sourceClient, target, links, tasks, options }
}

describe('cross-project Desktop thread tools', () => {
  it('lists reachable projects without opening them and restricts remote callers to their own project', async () => {
    const { call, list, connect } = fixture()
    expect(await call('ListProjects', {})).toMatchObject({ structuredContent: { projects: [chats, library] } })
    expect(await call('ListProjects', {}, { project: library, remote: true })).toMatchObject({
      structuredContent: { projects: [library] }
    })
    expect(await call('CreateThread', { initiation: 'userAsked', title: child.title, prompt: 'Run tests', project: library.id },
      { remote: true })).toMatchObject({ errorCode: 'TargetUnsupported' })
    expect(list).toHaveBeenCalledTimes(1)
    expect(connect).not.toHaveBeenCalled()
  })

  it('creates, lists, and finds linked children across projects without sending requests to the caller', async () => {
    const { call, connect, source, target } = fixture()
    expect(await call('CreateThread', { initiation: 'userAsked', title: child.title, prompt: 'Run tests', project: library.id }))
      .toMatchObject({ success: true, structuredContent: { thread: { id: child.threadId, workspacePath: library.path } } })
    expect(connect).toHaveBeenCalledWith(library)
    expect(target.mock.calls.map(([method, params]) => [method, params])).toEqual([
      ['thread/start', expect.objectContaining({
        identity: expect.objectContaining({ workspacePath: library.path }), cwd: '/projects/library/src', spawnedFromThreadId: parent.threadId
      })],
      ['turn/start', expect.objectContaining({ threadId: child.threadId })]
    ])

    expect(await call('ListThreads', { project: 'ignored', linked: true })).toMatchObject({
      structuredContent: { threads: [{ id: child.threadId, project: library.id, workspacePath: library.path }] }
    })
    expect(await call('SearchThreadMessages', { threadId: child.threadId, searchTerm: 'library' })).toMatchObject({
      structuredContent: { data: [{ occurrence: 'library' }] }
    })
    expect(target).toHaveBeenLastCalledWith('thread/searchOccurrences', { threadId: child.threadId, searchTerm: 'library', limit: 20 })
    expect(source).not.toHaveBeenCalled()
  })

  it('opens the project for an ordinary list and distinguishes unknown and unavailable projects', async () => {
    const { call, target, connect } = fixture()
    expect(await call('ListThreads', { project: library.id })).toMatchObject({ success: true,
      structuredContent: { nextCursor: 'next', totalMatched: 2 } })
    expect(target).toHaveBeenCalledWith('thread/list', expect.objectContaining({
      identity: expect.objectContaining({ workspacePath: library.path })
    }))
    expect(await call('ListThreads', { project: 'not-a-project' })).toMatchObject({ errorCode: 'ProjectNotFound' })
    connect.mockRejectedValueOnce(new Error('Startup failed'))
    expect(await call('ListThreads', { project: library.id })).toMatchObject({ errorCode: 'ProjectUnavailable' })
  })

  it('retains a proposal target until the user starts it, including worktree starts', async () => {
    const { call, composerTurn, tasks, sourceClient, options, connect, target, links } = fixture()
    composerTurn()
    expect(await call('CreateThread', { initiation: 'ownInitiative', title: child.title, prompt: 'Run tests', project: library.id }))
      .toMatchObject({ structuredContent: { task: { targetProjectId: library.id, targetWorkspacePath: library.path } } })
    expect(connect).not.toHaveBeenCalled()
    const task = tasks.snapshot(parent.threadId).tasks[0]
    const result = await tasks.startProposals(threadTaskConnection(sourceClient, chats.path, options), parent.threadId, [task.id],
      { workLocation: 'worktree' })
    expect(result).toMatchObject([{ ok: true, childThreadId: child.threadId }])
    const request = target.mock.calls.find(([method]) => method === 'worktree/createAndStart')
    expect(request?.[1]).toMatchObject({ cwd: '/projects/library/src' })
    expect(request?.[1]).not.toHaveProperty('spawnedFromThreadId')
    expect(links.find(parent, child.threadId)?.child).toMatchObject({ projectId: library.id, workspacePath: library.path })
  })

  it('keeps the target when an offer is accepted and briefs that same chat in a separate call', async () => {
    const { call, composerTurn, tasks, sourceClient, options, connect, target, links } = fixture()
    composerTurn()
    const offered = call('CreateThread', { initiation: 'offer', title: child.title, project: library.id })
    await vi.waitFor(() => expect(tasks.snapshot(parent.threadId).tasks[0]?.state).toBe('pending'))
    const { id } = tasks.snapshot(parent.threadId).tasks[0]
    expect(connect).not.toHaveBeenCalled()
    expect(await tasks.answerOffer(threadTaskConnection(sourceClient, chats.path, options), parent.threadId, id, { choice: 'newThread' }))
      .toEqual({ ok: true })
    expect(await offered).toMatchObject({ structuredContent: { outcome: 'accepted' } })
    expect(links.find(parent, child.threadId)?.child.projectId).toBe(library.id)
    expect(target.mock.calls.map(([method]) => method)).toEqual(['thread/start'])
    expect(await call('CreateThread', { proposalId: id, prompt: 'Run tests' })).toMatchObject({
      success: true, structuredContent: { thread: { id: child.threadId, workspacePath: library.path }, started: true }
    })
    expect(target.mock.calls.map(([method]) => method)).toEqual(['thread/start', 'turn/start'])
  })

  it('stops the active turn only in a linked chat, and makes no interrupt request for idle chats', async () => {
    const { call, target, connect, links } = fixture()
    expect(await call('StopThread', { threadId: child.threadId, project: library.id })).toMatchObject({ errorCode: 'NotLinked' })
    expect(await call('StopThread', { threadId: parent.threadId })).toMatchObject({ errorCode: 'NotLinked' })
    expect(connect).not.toHaveBeenCalled()
    links.link(parent, child)
    target.mockResolvedValueOnce({ thread: { id: child.threadId, runtime: { busy: false } } })
    expect(await call('StopThread', { threadId: child.threadId })).toMatchObject({
      success: true, structuredContent: { threadId: child.threadId, stopped: false }
    })
    expect(target.mock.calls.map(([method]) => method)).toEqual(['thread/read'])
    expect(await call('StopThread', { threadId: child.threadId })).toMatchObject({
      success: true, structuredContent: { threadId: child.threadId, stopped: true, workspacePath: library.path }
    })
    expect(target).toHaveBeenLastCalledWith('turn/interrupt', { threadId: child.threadId, turnId: 'turn-1' })
  })

  it('queues a follow-up to a busy linked chat and changes reasoning without taking over its binding', async () => {
    const { call, target, links } = fixture()
    links.link(parent, child)
    expect(await call('SendMessageToThread', { threadId: child.threadId, prompt: 'Also cover the retry', reasoningEffort: 'high' }))
      .toMatchObject({ success: true, structuredContent: { queued: true, queuedInput: { id: 'queued-1' }, workspacePath: library.path } })
    expect(target.mock.calls).toEqual([
      ['turn/startOrEnqueue', {
        threadId: child.threadId,
        input: [{ type: 'text', text: 'Also cover the retry' }],
        reasoning: { enabled: true, effort: 'high' }
      }]
    ])
  })
})
