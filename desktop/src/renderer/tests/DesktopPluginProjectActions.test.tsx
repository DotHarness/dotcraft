import type { DesktopPluginHost } from '@dotcraft/plugin'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import type { ProjectEnvironmentsResult } from '../../shared/projectActions'
import type { ViewerTab } from '../../shared/viewer/types'
import {
  createDesktopPluginHost,
  disposeDesktopPluginCleanupScope,
  type DesktopPluginCleanupScope
} from '../plugins/desktopPluginHost'
import type { PluginEntry } from '../stores/pluginStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { installDesktopApiMock } from './desktopApiMock'

const { requestDialog } = vi.hoisted(() => ({ requestDialog: vi.fn() }))

vi.mock('../components/projectActions/AddProjectActionDialog', () => ({
  requestAddProjectActionDialog: requestDialog
}))

const scopes: DesktopPluginCleanupScope[] = []
const WORKSPACE = 'C:\\repo'

function host(): DesktopPluginHost {
  const plugin = {
    id: 'fixture.desktop',
    displayName: 'Fixture',
    version: '1.0.0',
    desktop: { entry: './desktop/dist/index.mjs', revision: 'a'.repeat(64), styles: [] }
  } as unknown as PluginEntry
  const scope: DesktopPluginCleanupScope = { active: true, cleanups: new Set() }
  scopes.push(scope)
  return createDesktopPluginHost(plugin, plugin.id, 'a'.repeat(64), scope)
}

function result(actionIds: string[]): ProjectEnvironmentsResult {
  return {
    environments: [{
      id: '.craft/environments/environment.json',
      name: 'Web',
      path: `${WORKSPACE}\\.craft\\environments\\environment.json`,
      isDefault: true,
      actions: actionIds.map((id) => ({ id, name: id, icon: 'run', command: `npm run ${id}` })),
      revision: 'r1'
    }],
    selectedEnvironmentId: '.craft/environments/environment.json'
  }
}

function nonceOf(tab: ViewerTab): number | undefined {
  return tab.kind === 'terminal' ? tab.pendingRun?.nonce : undefined
}

function terminalTabs(threadId: string) {
  return useViewerTabStore.getState().getThreadState(threadId).tabs.filter((tab) => tab.kind === 'terminal')
}

beforeEach(() => {
  useConversationStore.setState({ workspacePath: WORKSPACE, remoteWorkspaceActive: false })
  useViewerTabStore.setState({ byThread: new Map() })
})

afterEach(() => {
  for (const scope of scopes.splice(0)) disposeDesktopPluginCleanupScope(scope)
})

describe('Desktop extension projectActions', () => {
  it('lists environments without their internal revision and rejects in a remote workspace', async () => {
    const list = vi.fn().mockResolvedValue(result(['dev']))
    installDesktopApiMock({ projectActions: { list } })
    const projectActions = host().projectActions

    const snapshot = await projectActions.list(WORKSPACE)

    expect(list).toHaveBeenCalledWith(WORKSPACE)
    expect(snapshot.selectedEnvironmentId).toBe('.craft/environments/environment.json')
    expect(snapshot.environments[0]).not.toHaveProperty('revision')
    expect(snapshot.environments[0].actions[0]).toEqual({ id: 'dev', name: 'dev', icon: 'run', command: 'npm run dev' })

    useConversationStore.setState({ remoteWorkspaceActive: true })
    await expect(projectActions.list(WORKSPACE)).rejects.toThrow()
    await expect(projectActions.run({ threadId: 't1', environmentId: 'e', actionId: 'a' })).rejects.toThrow()
  })

  it('runs an action in one titled terminal tab per thread, environment, and action', async () => {
    const prepareRun = vi.fn().mockResolvedValue({ cwd: WORKSPACE, command: 'npm test', title: 'Test' })
    installDesktopApiMock({ projectActions: { prepareRun } })
    const projectActions = host().projectActions
    const request = { threadId: 't1', environmentId: '.craft/environments/environment.json', actionId: 'Test' }

    await projectActions.run(request)
    const [first] = terminalTabs('t1')
    await projectActions.run(request)
    const tabs = terminalTabs('t1')

    expect(prepareRun).toHaveBeenCalledWith({
      workspacePath: WORKSPACE,
      environmentId: request.environmentId,
      actionId: request.actionId
    })
    expect(tabs).toHaveLength(1)
    expect(tabs[0]).toMatchObject({ id: first.id, label: 'Test', cwd: WORKSPACE, pendingRun: { command: 'npm test' } })
    expect(nonceOf(tabs[0])).not.toBe(nonceOf(first))
    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'viewer', id: first.id })

    await projectActions.run({ ...request, threadId: 't2' })
    expect(terminalTabs('t2')[0].id).not.toBe(first.id)
  })

  it('runs an action inside the worktree of a worktree thread', async () => {
    const prepareRun = vi.fn().mockResolvedValue({ cwd: 'C:\worktrees\repo-1', command: 'npm test', title: 'Test' })
    installDesktopApiMock({ projectActions: { prepareRun } })
    useConversationStore.setState({ workspacePath: 'C:\worktrees\repo-1' })
    useThreadStore.setState({
      threadList: [{ id: 'w1', workspacePath: WORKSPACE, worktree: { path: 'C:\worktrees\repo-1' } } as never]
    })

    await host().projectActions.run({ threadId: 'w1', environmentId: 'e', actionId: 'Test' })

    expect(prepareRun).toHaveBeenCalledWith({
      workspacePath: WORKSPACE,
      environmentId: 'e',
      actionId: 'Test',
      worktreePath: 'C:\worktrees\repo-1'
    })
  })

  it('selects No environment', async () => {
    const select = vi.fn().mockResolvedValue(undefined)
    installDesktopApiMock({ projectActions: { select } })

    await host().projectActions.select(WORKSPACE, null)

    expect(select).toHaveBeenCalledWith({ workspacePath: WORKSPACE, environmentId: null })
  })

  it('opens environment settings and remembers the conversation to return to', async () => {
    installDesktopApiMock({})
    useUIStore.setState({ activeMainView: 'conversation', environmentSettingsTarget: null })

    await host().projectActions.openSettings({ workspacePath: WORKSPACE, environmentId: 'e' })

    const ui = useUIStore.getState()
    expect(ui.activeMainView).toBe('settings')
    expect(ui.activeSettingsTab).toBe('worktrees')
    expect(ui.environmentSettingsTarget).toMatchObject({
      projectPath: WORKSPACE,
      environmentId: 'e',
      returnTo: { kind: 'conversation' }
    })
  })

  it('opens a plain terminal tab at the workspace root', async () => {
    installDesktopApiMock({})
    await host().projectActions.openTerminal({ threadId: 't1' })

    const [tab] = terminalTabs('t1')
    expect(tab).toMatchObject({ cwd: WORKSPACE, hasStarted: false })
    expect(tab.kind === 'terminal' && tab.pendingRun).toBeFalsy()
    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'viewer', id: tab.id })
  })

  it('notifies only when the listed result changes and stops after disposal', async () => {
    let changed: (() => void) | null = null
    const unwatch = vi.fn()
    const list = vi.fn()
      .mockResolvedValueOnce(result(['dev']))
      .mockResolvedValueOnce(result(['dev']))
      .mockResolvedValueOnce(result(['test', 'dev']))
    installDesktopApiMock({
      projectActions: {
        list,
        watch: (_workspacePath: string, listener: () => void) => {
          changed = listener
          return unwatch
        }
      }
    })
    const listener = vi.fn()
    const dispose = host().projectActions.onChange(WORKSPACE, listener)
    await vi.waitFor(() => expect(list).toHaveBeenCalledTimes(1))

    changed!()
    await vi.waitFor(() => expect(list).toHaveBeenCalledTimes(2))
    changed!()
    await vi.waitFor(() => expect(listener).toHaveBeenCalledTimes(1))
    expect(listener.mock.calls[0][0].environments[0].actions.map((action: { id: string }) => action.id)).toEqual(['test', 'dev'])

    dispose()
    expect(unwatch).toHaveBeenCalledTimes(1)
  })

  it('opens the add-action dialog with the revision of the chosen environment', async () => {
    installDesktopApiMock({ projectActions: { list: vi.fn().mockResolvedValue(result([])) } })
    requestDialog.mockReturnValue({
      result: Promise.resolve({
        ok: true,
        environmentId: '.craft/environments/environment.json',
        action: { id: 'Dev', name: 'Dev', icon: 'run', command: 'npm run dev' }
      }),
      dismiss: () => {}
    })

    const saved = await host().projectActions.promptAddAction({ workspacePath: WORKSPACE })

    expect(requestDialog).toHaveBeenCalledWith({
      workspacePath: WORKSPACE,
      environmentId: '.craft/environments/environment.json',
      revision: 'r1'
    })
    expect(saved).toEqual({
      environmentId: '.craft/environments/environment.json',
      action: { id: 'Dev', name: 'Dev', icon: 'run', command: 'npm run dev' }
    })
  })
})
