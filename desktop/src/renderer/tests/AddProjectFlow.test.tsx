import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useAddProjectFlow } from '../components/projects/AddProject'
import type { WorkspaceProjectSummary } from '../../shared/workspaceProjects'
import { installDesktopApiMock } from './desktopApiMock'

const pickFolder = vi.fn()
const switchWorkspace = vi.fn()
const createLocalProject = vi.fn()
const saveLocalProject = vi.fn()
const removeRecent = vi.fn()

const editProject: WorkspaceProjectSummary = {
  path: '/projects/app',
  name: 'app',
  secondaryFolders: ['/projects/shared'],
  state: 'foreground',
  running: true,
  loaded: true,
  threadCount: 0,
  threads: [],
  pinned: false
}

function Harness(): JSX.Element {
  const flow = useAddProjectFlow()
  return (
    <div>
      <button type="button" onClick={flow.beginCreate}>open-create</button>
      <button type="button" onClick={() => flow.beginEdit(editProject, true)}>open-edit-active</button>
      <button type="button" onClick={() => flow.beginEdit(editProject, false)}>open-edit-inactive</button>
      {flow.dialog}
    </div>
  )
}

function renderHarness(): void {
  render(
    <LocaleProvider>
      <Harness />
    </LocaleProvider>
  )
}

describe('Create / Edit project flow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    pickFolder.mockResolvedValue(null)
    switchWorkspace.mockResolvedValue(undefined)
    createLocalProject.mockResolvedValue({ path: 'C:/Users/me/Documents/My App', gitInitialized: true })
    saveLocalProject.mockImplementation(async ({ primaryFolder }: { primaryFolder: string }) => ({ path: primaryFolder }))
    removeRecent.mockResolvedValue(undefined)
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }), set: vi.fn() },
      workspace: { pickFolder, switch: switchWorkspace, createLocalProject, saveLocalProject, removeRecent }
    })
  })

  it('creates a from-scratch project from a name only, then switches to it', async () => {
    renderHarness()
    fireEvent.click(screen.getByText('open-create'))

    const input = await screen.findByPlaceholderText('New project')
    fireEvent.change(input, { target: { value: 'My App' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create project' }))

    await waitFor(() => expect(createLocalProject).toHaveBeenCalledWith({ name: 'My App' }))
    await waitFor(() => expect(switchWorkspace).toHaveBeenCalledWith('C:/Users/me/Documents/My App'))
    expect(saveLocalProject).not.toHaveBeenCalled()
  })

  it('creates a project from an attached existing folder, then switches to it', async () => {
    pickFolder.mockResolvedValue('D:/projects/legacy')
    renderHarness()
    fireEvent.click(screen.getByText('open-create'))

    // Empty state: the add target opens the native picker.
    fireEvent.click(await screen.findByRole('button', { name: 'Add folders DotCraft can read and edit' }))
    await screen.findByText('D:/projects/legacy')

    fireEvent.click(screen.getByRole('button', { name: 'Create project' }))

    await waitFor(() =>
      expect(saveLocalProject).toHaveBeenCalledWith(
        expect.objectContaining({ primaryFolder: 'D:/projects/legacy', secondaryFolders: [] })
      )
    )
    await waitFor(() => expect(switchWorkspace).toHaveBeenCalledWith('D:/projects/legacy'))
    expect(createLocalProject).not.toHaveBeenCalled()
  })

  it('keeps the Create action disabled until a name or folder is provided', async () => {
    renderHarness()
    fireEvent.click(screen.getByText('open-create'))

    const create = await screen.findByRole('button', { name: 'Create project' })
    expect(create).toBeDisabled()
  })

  it('edits a project: reordering the primary saves the full ordered folder list', async () => {
    renderHarness()
    fireEvent.click(screen.getByText('open-edit-active'))

    // The secondary folder exposes "Make primary"; promoting it reorders the list.
    fireEvent.click(await screen.findByRole('button', { name: 'Make primary' }))
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() =>
      expect(saveLocalProject).toHaveBeenCalledWith(
        expect.objectContaining({
          previousPath: '/projects/app',
          primaryFolder: '/projects/shared',
          secondaryFolders: ['/projects/app']
        })
      )
    )
    // Primary changed on the active project → re-open the new primary.
    await waitFor(() => expect(switchWorkspace).toHaveBeenCalledWith('/projects/shared'))
  })

  it('removes a project from the edit dialog', async () => {
    renderHarness()
    fireEvent.click(screen.getByText('open-edit-inactive'))

    fireEvent.click(await screen.findByRole('button', { name: 'Remove project' }))

    await waitFor(() => expect(removeRecent).toHaveBeenCalledWith('/projects/app'))
  })
})
