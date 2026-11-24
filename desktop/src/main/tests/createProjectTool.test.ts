import { existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import * as path from 'node:path'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { normalizeWorkspaceProjectKey, sameWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import {
  DESKTOP_CONVERSATION_TOOL_NAMESPACE,
  handleDesktopRuntimeThreadToolCall,
  type AppServerRequestClient,
  type DesktopAppServerRequestOptions
} from '../desktopRuntimeThreadTools'
import {
  createNewLocalProjectFolder,
  planNewLocalProjectFolder,
  registerLocalProject
} from '../localProjectOperations'
import { DesktopToolConfirmManager } from '../desktopToolConfirm'
import type { AppSettings } from '../settings'
import type { DesktopProjectCreatorHost } from '../threadTasks/projectTools'
import { DesktopThreadTasks } from '../threadTasks/threadTaskService'
import { ThreadTaskStore } from '../threadTasks/threadTaskStore'
import { desktopThreadToolOptions } from '../windows/windowRequests'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import type { WorkspaceConnectionEntry } from '../windows/windowTypes'

describe('CreateProject desktop tool', () => {
  let rootDir = ''

  beforeEach(() => {
    rootDir = mkdtempSync(path.join(tmpdir(), 'dotcraft-create-project-'))
  })

  afterEach(() => {
    if (rootDir) rmSync(rootDir, { recursive: true, force: true })
  })

  function createFixture() {
    const documentsDir = path.join(rootDir, 'Documents')
    const defaultChatsDir = path.join(rootDir, 'default-chats')
    mkdirSync(documentsDir, { recursive: true })
    mkdirSync(defaultChatsDir, { recursive: true })
    const hubProjects: string[] = []
    const settings: AppSettings = {}
    const persistSettings = vi.fn()
    const broadcastProjects = vi.fn()
    const initGit = vi.fn(async () => undefined)
    const confirmCreation = vi.fn<DesktopProjectCreatorHost['confirmCreation']>(async () => 'confirmed')

    const client = {
      sendRequest: vi.fn(async () => {
        throw new Error('Unexpected AppServer request')
      })
    } as AppServerRequestClient

    const tasks = new DesktopThreadTasks({
      store: new ThreadTaskStore(null),
      getNewTaskPrompt: () => 'ask'
    })

    const projectCreator: DesktopProjectCreatorHost = {
      refreshProjects: vi.fn(async () => undefined),
      existingProjectId: (projectPath) =>
        hubProjects.some((candidate) => sameWorkspaceProjectKey(candidate, projectPath))
          ? normalizeWorkspaceProjectKey(projectPath)
          : undefined,
      isDefaultChat: (folder) => sameWorkspaceProjectKey(folder, defaultChatsDir),
      planNewFolder: (name) => planNewLocalProjectFolder(name, documentsDir),
      createNewFolder: (name, preferredPath) =>
        createNewLocalProjectFolder(name, { baseDir: documentsDir, preferredPath, initGit }),
      confirmCreation,
      registerProject: (params) =>
        registerLocalProject({
          openInHub: async (projectPath) => {
            if (!hubProjects.some((candidate) => sameWorkspaceProjectKey(candidate, projectPath))) {
              hubProjects.push(projectPath)
            }
          },
          settings,
          persistSettings,
          broadcastProjects
        }, params)
    }

    const markComposerTurn = (threadId = 'thread-1', turnId = 'turn-1') => {
      tasks.handleNotification('turn/started', {
        turn: {
          id: turnId,
          threadId,
          originChannel: 'dotcraft-desktop',
          items: [{ type: 'userMessage', payload: { text: 'Create a project' } }]
        }
      })
    }

    const call = (
      args: Record<string, unknown>,
      overrides: DesktopAppServerRequestOptions = {},
      callMeta: { threadId?: string; turnId?: string } = {}
    ) =>
      handleDesktopRuntimeThreadToolCall(client, {
        namespace: DESKTOP_CONVERSATION_TOOL_NAMESPACE,
        threadId: callMeta.threadId ?? 'thread-1',
        turnId: callMeta.turnId ?? 'turn-1',
        tool: 'CreateProject',
        arguments: args
      }, path.join(rootDir, 'current-workspace'), {
        tasks,
        projectCreator,
        ...overrides
      })

    return {
      documentsDir,
      defaultChatsDir,
      hubProjects,
      settings,
      persistSettings,
      broadcastProjects,
      initGit,
      confirmCreation,
      tasks,
      markComposerTurn,
      call
    }
  }

  it('creates a new folder with Git after user confirmation and registers the project', async () => {
    const fixture = createFixture()
    fixture.markComposerTurn()

    const result = await fixture.call({ name: 'Weather App' })
    const expectedPath = path.join(fixture.documentsDir, 'Weather App')

    expect(fixture.confirmCreation).toHaveBeenCalledWith({
      threadId: 'thread-1',
      turnId: 'turn-1',
      name: 'Weather App',
      folders: [expectedPath],
      createsNewFolder: true
    })
    expect(existsSync(expectedPath)).toBe(true)
    expect(fixture.initGit).toHaveBeenCalledWith(expectedPath)
    expect(fixture.hubProjects).toEqual([expectedPath])
    expect(fixture.persistSettings).toHaveBeenCalledTimes(1)
    expect(fixture.broadcastProjects).toHaveBeenCalledTimes(1)
    expect(result).toMatchObject({
      success: true,
      structuredContent: {
        createdNewFolder: true,
        gitInitialized: true,
        project: {
          id: normalizeWorkspaceProjectKey(expectedPath),
          name: 'Weather App',
          path: expectedPath,
          primaryFolder: expectedPath,
          secondaryFolders: []
        }
      }
    })
  })

  it('attaches existing multi-folder paths, deduplicates them, and promotes primaryFolder', async () => {
    const fixture = createFixture()
    fixture.markComposerTurn()
    const folderA = path.join(rootDir, 'alpha')
    const folderB = path.join(rootDir, 'beta')
    mkdirSync(folderA)
    mkdirSync(folderB)

    const result = await fixture.call({
      name: 'Workspace',
      folders: [folderA, folderB, folderA],
      primaryFolder: folderB
    })

    expect(fixture.confirmCreation).toHaveBeenCalledWith({
      threadId: 'thread-1',
      turnId: 'turn-1',
      name: 'Workspace',
      folders: [folderB, folderA],
      createsNewFolder: false
    })
    expect(fixture.initGit).not.toHaveBeenCalled()
    expect(result).toMatchObject({
      success: true,
      structuredContent: {
        createdNewFolder: false,
        project: {
          id: normalizeWorkspaceProjectKey(folderB),
          name: 'Workspace',
          path: folderB,
          primaryFolder: folderB,
          secondaryFolders: [folderA]
        }
      }
    })
    expect(fixture.settings.localProjectDetails?.[normalizeWorkspaceProjectKey(folderB)]).toEqual({
      name: 'Workspace',
      primaryFolder: folderB,
      secondaryFolders: [folderA]
    })
  })

  it('leaves disk and Hub untouched when the user cancels or confirmation is abandoned', async () => {
    const fixture = createFixture()
    fixture.markComposerTurn()
    fixture.confirmCreation.mockResolvedValueOnce('canceled')

    const canceled = await fixture.call({ name: 'Canceled Project' })
    expect(canceled).toMatchObject({ success: false, errorCode: 'ProjectCreationCanceled' })
    expect(existsSync(path.join(fixture.documentsDir, 'Canceled Project'))).toBe(false)
    expect(fixture.hubProjects).toEqual([])

    fixture.confirmCreation.mockResolvedValueOnce('abandoned')
    const abandoned = await fixture.call({ name: 'Abandoned Project' })
    expect(abandoned).toMatchObject({ success: false, errorCode: 'ProjectCreationCanceled' })
    expect(existsSync(path.join(fixture.documentsDir, 'Abandoned Project'))).toBe(false)
  })

  it('rejects remote callers and non-composer turns without showing confirmation', async () => {
    const fixture = createFixture()

    const remote = await fixture.call({ name: 'Remote Attempt' }, { remote: true })
    expect(remote).toMatchObject({ success: false, errorCode: 'TargetUnsupported' })

    fixture.tasks.handleNotification('turn/started', {
      turn: {
        id: 'turn-auto',
        threadId: 'thread-auto',
        originChannel: 'automation',
        items: [{ type: 'userMessage', payload: { text: 'Auto', triggerKind: 'automation' } }]
      }
    })
    const nonComposer = await fixture.call({ name: 'Auto Attempt' }, {}, { threadId: 'thread-auto', turnId: 'turn-auto' })
    expect(nonComposer).toMatchObject({ success: false, errorCode: 'TargetUnsupported' })
    expect(fixture.confirmCreation).not.toHaveBeenCalled()
  })

  it('rejects invalid arguments, missing directories, the default Chats workspace, and duplicate projects', async () => {
    const fixture = createFixture()
    fixture.markComposerTurn()

    expect(await fixture.call({})).toMatchObject({ success: false, errorCode: 'InvalidArguments' })
    expect(await fixture.call({ name: 'Empty', folders: [] })).toMatchObject({ success: false, errorCode: 'InvalidArguments' })
    expect(await fixture.call({ name: 'OnlyPrimary', primaryFolder: rootDir })).toMatchObject({ success: false, errorCode: 'InvalidArguments' })
    expect(await fixture.call({ folders: ['relative/path'] })).toMatchObject({ success: false, errorCode: 'InvalidArguments' })
    expect(await fixture.call({ folders: [path.join(rootDir, 'does-not-exist')] })).toMatchObject({ success: false, errorCode: 'InvalidArguments' })

    const filePath = path.join(rootDir, 'file.txt')
    writeFileSync(filePath, 'hello')
    expect(await fixture.call({ folders: [filePath] })).toMatchObject({ success: false, errorCode: 'InvalidArguments' })

    const folderA = path.join(rootDir, 'a')
    const folderB = path.join(rootDir, 'b')
    mkdirSync(folderA)
    mkdirSync(folderB)
    expect(await fixture.call({ folders: [folderA], primaryFolder: folderB })).toMatchObject({
      success: false,
      errorCode: 'InvalidArguments'
    })

    expect(await fixture.call({ folders: [fixture.defaultChatsDir] })).toMatchObject({ success: false, errorCode: 'TargetUnsupported' })

    fixture.hubProjects.push(folderA)
    expect(await fixture.call({ folders: [folderA] })).toMatchObject({ success: false, errorCode: 'ProjectAlreadyExists' })
    expect(fixture.confirmCreation).not.toHaveBeenCalled()
  })
})

describe('DesktopToolConfirmManager', () => {
  it('serializes confirmations per window, verifies window ownership, and abandons on turn end or owner teardown', async () => {
    const manager = new DesktopToolConfirmManager()
    const sent: Array<{ requestId: string; name: string }> = []
    const dismissed: string[] = []
    const confirm = (turnId: string, name: string) => manager.confirm({
      ownerId: 1,
      threadId: 'thread-1',
      turnId,
      details: { kind: 'projectCreation', name, folders: [`/tmp/${name}`], createsNewFolder: true },
      sendToRenderer: (request) => {
        sent.push({ requestId: request.requestId, name: request.kind === 'projectCreation' ? request.name : '' })
        return true
      },
      dismissInRenderer: (requestId) => dismissed.push(requestId)
    })

    const first = confirm('turn-1', 'First')
    const second = confirm('turn-2', 'Second')

    await vi.waitFor(() => expect(sent).toHaveLength(1))
    expect(sent[0].name).toBe('First')

    expect(manager.respond(2, sent[0].requestId, true)).toBe(false)
    expect(manager.respond(1, sent[0].requestId, true)).toBe(true)
    expect(await first).toBe('confirmed')

    await vi.waitFor(() => expect(sent).toHaveLength(2))
    expect(sent[1].name).toBe('Second')

    manager.observeNotification('turn/cancelled', { threadId: 'thread-1', turnId: 'turn-2' })
    expect(await second).toBe('abandoned')
    expect(dismissed).toContain(sent[1].requestId)
  })
})

describe('desktopThreadToolOptions projectCreator gating', () => {
  function buildRuntime(argv: string[] = []): DesktopWindowRuntime {
    return {
      argv,
      settings: {},
      workspaceConnections: new Map(),
      lastConnectionStatus: {},
      app: {
        localProjects: { list: () => [], has: () => false, refresh: async () => true, open: async () => undefined },
        toolConfirm: new DesktopToolConfirmManager(),
        persistWindowSettings: () => undefined,
        broadcastWorkspaceProjects: () => undefined,
        broadcastPinnedThreadIdsChanged: () => undefined
      },
      isLocalChatsForeground: () => false,
      getThreadTasks: () => undefined,
      updateSettings: async () => undefined,
      localProjectId: (value: string) => normalizeWorkspaceProjectKey(value)
    } as unknown as DesktopWindowRuntime
  }

  function entry(kind: WorkspaceConnectionEntry['kind']): WorkspaceConnectionEntry {
    return {
      key: `${kind}:ws`,
      projectId: `${kind}:ws`,
      kind,
      workspacePath: '/workspace/chats',
      wsUrl: 'ws://127.0.0.1:1',
      client: {} as WorkspaceConnectionEntry['client'],
      role: 'foreground',
      connected: true,
      connecting: false,
      lastUsedAt: 0,
      threads: []
    }
  }

  it('exposes projectCreator for chat and local workspaces and omits it for remote connections or --remote launches', () => {
    const runtime = buildRuntime()
    expect(desktopThreadToolOptions(runtime, entry('chat')).projectCreator).toBeDefined()
    expect(desktopThreadToolOptions(runtime, entry('local')).projectCreator).toBeDefined()
    expect(desktopThreadToolOptions(runtime, entry('remote')).projectCreator).toBeUndefined()
    expect(desktopThreadToolOptions(buildRuntime(['--remote', 'ws://example']), entry('chat')).projectCreator).toBeUndefined()
  })
})
