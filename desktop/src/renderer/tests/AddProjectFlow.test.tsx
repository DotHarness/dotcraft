import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { AddProjectMenuOptions, useAddProjectFlow } from '../components/projects/AddProject'

const pickFolder = vi.fn()
const switchWorkspace = vi.fn()
const createLocalProject = vi.fn()

function Harness(): JSX.Element {
  const flow = useAddProjectFlow()
  return (
    <div>
      <AddProjectMenuOptions
        onStartFromScratch={flow.beginScratch}
        onUseExistingFolder={() => { void flow.chooseExistingFolder() }}
        disabled={flow.busy}
      />
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

describe('Add project flow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    pickFolder.mockResolvedValue(null)
    switchWorkspace.mockResolvedValue(undefined)
    createLocalProject.mockResolvedValue({ path: 'C:/Users/me/Documents/My App', gitInitialized: true })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }), set: vi.fn() },
        workspace: { pickFolder, switch: switchWorkspace, createLocalProject }
      }
    })
  })

  it('creates a from-scratch project from the name dialog, then switches to it', async () => {
    renderHarness()

    fireEvent.click(screen.getByRole('menuitem', { name: 'Start from scratch' }))

    // The name dialog opens pre-filled with the default project name.
    const input = await screen.findByDisplayValue('New project')
    fireEvent.change(input, { target: { value: 'My App' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(createLocalProject).toHaveBeenCalledWith({ name: 'My App' }))
    await waitFor(() => expect(switchWorkspace).toHaveBeenCalledWith('C:/Users/me/Documents/My App'))
  })

  it('opens an existing folder via the native picker, then switches to it', async () => {
    pickFolder.mockResolvedValue('D:/projects/legacy')
    renderHarness()

    fireEvent.click(screen.getByRole('menuitem', { name: 'Use an existing folder' }))

    await waitFor(() => expect(pickFolder).toHaveBeenCalled())
    await waitFor(() => expect(switchWorkspace).toHaveBeenCalledWith('D:/projects/legacy'))
    expect(createLocalProject).not.toHaveBeenCalled()
  })

  it('does not switch when the folder picker is cancelled', async () => {
    pickFolder.mockResolvedValue(null)
    renderHarness()

    fireEvent.click(screen.getByRole('menuitem', { name: 'Use an existing folder' }))

    await waitFor(() => expect(pickFolder).toHaveBeenCalled())
    expect(switchWorkspace).not.toHaveBeenCalled()
  })
})
