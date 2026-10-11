import '../../../renderer/tests/setupPluginRuntime'
import type { DesktopPluginHost, DesktopPluginProjectActionsSnapshot } from '@dotcraft/plugin'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { EnvironmentSection } from './EnvironmentSection'
import { stringsFor } from './i18n'

const WORKSPACE = '/repo'
const DEFAULT_ID = '.craft/environments/environment.json'

function snapshot(actionIds: string[], extra = false): DesktopPluginProjectActionsSnapshot {
  const environment = (id: string, name: string) => ({
    id,
    name,
    path: `${WORKSPACE}/${id}`,
    isDefault: id === DEFAULT_ID,
    actions: actionIds.map((action) => ({ id: action, name: action, icon: 'run' as const, command: action }))
  })
  return {
    environments: extra
      ? [environment(DEFAULT_ID, 'Web'), environment('.craft/environments/ci.json', 'CI')]
      : actionIds.length > 0 ? [environment(DEFAULT_ID, 'Web')] : [],
    selectedEnvironmentId: actionIds.length > 0 || extra ? DEFAULT_ID : null
  }
}

function setup(list: Promise<DesktopPluginProjectActionsSnapshot>) {
  const projectActions = {
    list: vi.fn(() => list),
    onChange: vi.fn(() => () => {}),
    select: vi.fn(async () => {}),
    run: vi.fn(async () => {}),
    openTerminal: vi.fn(async () => {}),
    promptAddAction: vi.fn(async () => ({
      environmentId: DEFAULT_ID,
      action: { id: 'Dev', name: 'Dev', icon: 'run' as const, command: 'npm run dev' }
    }))
  }
  const host = { projectActions, navigation: { openFile: vi.fn(async () => {}) } } as unknown as DesktopPluginHost
  render(<EnvironmentSection host={host} threadId="t1" workspacePath={WORKSPACE} strings={stringsFor('en')} />)
  return projectActions
}

describe('Summary environment section', () => {
  it('runs the most recent action from the primary button', async () => {
    const projectActions = setup(Promise.resolve(snapshot(['Test', 'Build'])))

    fireEvent.click(await screen.findByRole('button', { name: 'Run: Test' }))

    expect(projectActions.run).toHaveBeenCalledWith({ threadId: 't1', environmentId: DEFAULT_ID, actionId: 'Test' })
  })

  it('sets up an environment through the add-action dialog and runs the saved action', async () => {
    const projectActions = setup(Promise.resolve(snapshot([])))

    fireEvent.click(await screen.findByRole('button', { name: 'Set up local environment' }))

    await waitFor(() => expect(projectActions.run).toHaveBeenCalledWith({
      threadId: 't1',
      environmentId: DEFAULT_ID,
      actionId: 'Dev'
    }))
    expect(projectActions.promptAddAction).toHaveBeenCalledWith({ workspacePath: WORKSPACE, environmentId: undefined })
  })

  it('changes the selected environment from the actions menu', async () => {
    const projectActions = setup(Promise.resolve(snapshot(['Test'], true)))

    fireEvent.click(await screen.findByRole('button', { name: 'Actions' }))
    fireEvent.click(await screen.findByRole('menuitemradio', { name: 'CI' }))

    expect(projectActions.select).toHaveBeenCalledWith(WORKSPACE, '.craft/environments/ci.json')
  })

  it('offers nothing when project actions are unavailable for the workspace', async () => {
    const projectActions = setup(Promise.reject(new Error('remote')))
    await waitFor(() => expect(projectActions.list).toHaveBeenCalled())
    expect(screen.queryByRole('button', { name: 'Open terminal' })).toBeNull()
  })
})
