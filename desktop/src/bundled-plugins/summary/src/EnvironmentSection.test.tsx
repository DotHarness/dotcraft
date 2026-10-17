import '../../../renderer/tests/setupPluginRuntime'
import type {
  DesktopPluginHost,
  DesktopPluginProjectActionsSnapshot,
  DesktopPluginProjectEnvironment
} from '@dotcraft/plugin'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { EnvironmentSection } from './EnvironmentSection'
import { stringsFor } from './i18n'
import type { ChangeTotals } from './sources'

const WORKSPACE = '/repo'
const DEFAULT_ID = '.craft/environments/environment.json'
const CI_ID = '.craft/environments/ci.json'

function environment(id: string, actionIds: string[], error?: string): DesktopPluginProjectEnvironment {
  return {
    id,
    name: id === DEFAULT_ID ? 'Web' : 'CI',
    path: `${WORKSPACE}/${id}`,
    isDefault: id === DEFAULT_ID,
    ...(error ? { error } : {}),
    actions: actionIds.map((action) => ({ id: action, name: action, icon: 'run' as const, command: action }))
  }
}

function snapshot(environments: DesktopPluginProjectEnvironment[]): DesktopPluginProjectActionsSnapshot {
  return { environments, selectedEnvironmentId: environments[0]?.id ?? null }
}

function setup(list: Promise<DesktopPluginProjectActionsSnapshot>, changes: ChangeTotals | null = null) {
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
  const navigation = { openFile: vi.fn(async () => {}), openDetailPanel: vi.fn() }
  const host = { projectActions, navigation } as unknown as DesktopPluginHost
  const view = render(
    <EnvironmentSection host={host} threadId="t1" workspacePath={WORKSPACE} changes={changes} strings={stringsFor('en')} />
  )
  return { projectActions, navigation, view }
}

async function openActions(): Promise<string[]> {
  fireEvent.click(await screen.findByRole('button', { name: 'Actions' }))
  await screen.findByRole('menuitem', { name: 'Open terminal' })
  return screen.getAllByRole('menuitem').map((item) => item.textContent ?? '')
}

describe('Summary environment section', () => {
  it('runs the most recent action from the primary button', async () => {
    const { projectActions } = setup(Promise.resolve(snapshot([environment(DEFAULT_ID, ['Test', 'Build'])])))

    fireEvent.click(await screen.findByRole('button', { name: 'Run: Test' }))

    expect(projectActions.run).toHaveBeenCalledWith({ threadId: 't1', environmentId: DEFAULT_ID, actionId: 'Test' })
  })

  it('lists the actions, adding an action and opening a terminal from the menu', async () => {
    const { projectActions } = setup(Promise.resolve(snapshot([environment(DEFAULT_ID, ['Test', 'Build'])])))

    expect(await openActions()).toEqual(['Test', 'Build', 'Add action', 'Open terminal'])
    fireEvent.click(screen.getByRole('menuitem', { name: 'Open terminal' }))

    expect(projectActions.openTerminal).toHaveBeenCalledWith({ threadId: 't1' })
  })

  it('sets up an environment from the menu and runs the saved action', async () => {
    const { projectActions } = setup(Promise.resolve(snapshot([])))

    expect(await openActions()).toEqual(['Set up local environment', 'Open terminal'])
    expect(screen.queryByRole('button', { name: /^Run:/ })).toBeNull()
    fireEvent.click(screen.getByRole('menuitem', { name: 'Set up local environment' }))

    await waitFor(() => expect(projectActions.run).toHaveBeenCalledWith({
      threadId: 't1',
      environmentId: DEFAULT_ID,
      actionId: 'Dev'
    }))
    expect(projectActions.promptAddAction).toHaveBeenCalledWith({ workspacePath: WORKSPACE, environmentId: undefined })
  })

  it('opens a broken environment file from the menu', async () => {
    const { navigation } = setup(Promise.resolve(snapshot([environment(DEFAULT_ID, [], 'Invalid JSON')])))

    expect(await openActions()).toEqual(['Fix local environment', 'Open terminal'])
    expect(screen.queryByRole('button', { name: /^Run:/ })).toBeNull()
    fireEvent.click(screen.getByRole('menuitem', { name: 'Fix local environment' }))

    expect(navigation.openFile).toHaveBeenCalledWith(`${WORKSPACE}/${DEFAULT_ID}`)
  })

  it('changes the selected environment from the actions menu', async () => {
    const { projectActions } = setup(Promise.resolve(snapshot([environment(DEFAULT_ID, ['Test']), environment(CI_ID, ['Test'])])))

    fireEvent.click(await screen.findByRole('button', { name: 'Actions' }))
    fireEvent.click(await screen.findByRole('menuitemradio', { name: 'CI' }))

    expect(projectActions.select).toHaveBeenCalledWith(WORKSPACE, CI_ID)
  })

  it('opens the changes of the conversation', async () => {
    const { navigation } = setup(Promise.resolve(snapshot([environment(DEFAULT_ID, ['Test'])])), { additions: 12, deletions: 3 })

    await screen.findByRole('button', { name: 'Actions' })
    fireEvent.click(screen.getByRole('button', { name: /^Changes/ }))

    expect(navigation.openDetailPanel).toHaveBeenCalledWith('changes')
  })

  it('keeps only the changes when project actions are unavailable', async () => {
    const { projectActions } = setup(Promise.reject(new Error('remote')), { additions: 1, deletions: 0 })
    await waitFor(() => expect(projectActions.list).toHaveBeenCalled())

    expect(screen.getByRole('button', { name: /^Changes/ })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Actions' })).toBeNull()
  })

  it('shows nothing without project actions or changes', async () => {
    const { projectActions, view } = setup(Promise.reject(new Error('remote')))
    await waitFor(() => expect(projectActions.list).toHaveBeenCalled())

    expect(view.container.firstChild).toBeNull()
  })
})
