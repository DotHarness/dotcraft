import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { OpenWorkspaceButton } from '../components/conversation/OpenWorkspaceButton'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const shellListEditors = vi.fn()
const shellLaunchEditor = vi.fn()

function renderButton(): void {
  render(
    <LocaleProvider>
      <OpenWorkspaceButton workspacePath={'F:\\dotcraft'} />
    </LocaleProvider>
  )
}

describe('OpenWorkspaceButton', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en', lastOpenEditorId: 'explorer' })
    settingsSet.mockResolvedValue(undefined)
    shellListEditors.mockResolvedValue([
      {
        id: 'cursor',
        labelKey: 'editors.cursor',
        iconKey: 'editor-generic',
        iconDataUrl: 'data:image/png;base64,cursor'
      },
      { id: 'explorer', labelKey: 'editors.explorer', iconKey: 'explorer' }
    ])
    shellLaunchEditor.mockResolvedValue(undefined)

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: settingsGet,
          set: settingsSet
        },
        shell: {
          listEditors: shellListEditors,
          launchEditor: shellLaunchEditor
        }
      }
    })
  })

  it('opens the dropdown and shows detected editors', async () => {
    renderButton()

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Choose how to open workspace' })).toBeEnabled()
    })
    fireEvent.click(screen.getByRole('button', { name: 'Choose how to open workspace' }))

    await waitFor(() => {
      expect(screen.getByRole('menuitem', { name: 'Cursor' })).toBeInTheDocument()
      expect(screen.getByRole('menuitem', { name: 'File Explorer' })).toBeInTheDocument()
    })
  })

  it('places File Explorer at the top of the dropdown', async () => {
    renderButton()

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Choose how to open workspace' })).toBeEnabled()
    })
    fireEvent.click(screen.getByRole('button', { name: 'Choose how to open workspace' }))

    await waitFor(() => {
      const menuItems = screen.getAllByRole('menuitem')
      expect(menuItems[0]).toHaveTextContent('File Explorer')
    })
  })

  it('renders image icons when iconDataUrl is available', async () => {
    renderButton()

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Choose how to open workspace' })).toBeEnabled()
    })
    fireEvent.click(screen.getByRole('button', { name: 'Choose how to open workspace' }))

    await waitFor(() => {
      expect(document.querySelector('img[src="data:image/png;base64,cursor"]')).toBeInTheDocument()
    })
  })

  it('switches default editor without launching on menu click', async () => {
    renderButton()

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Choose how to open workspace' })).toBeEnabled()
    })
    fireEvent.click(screen.getByRole('button', { name: 'Choose how to open workspace' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Cursor' }))

    await waitFor(() => {
      expect(shellLaunchEditor).not.toHaveBeenCalled()
      expect(settingsSet).toHaveBeenCalledWith({ lastOpenEditorId: 'cursor' })
    })
  })

  it('launches current default editor on primary click', async () => {
    settingsGet.mockResolvedValue({ locale: 'en', lastOpenEditorId: 'cursor' })
    renderButton()

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Open in Cursor' })).toBeEnabled()
    })
    fireEvent.click(screen.getByRole('button', { name: 'Open in Cursor' }))

    await waitFor(() => {
      expect(shellLaunchEditor).toHaveBeenCalledWith('cursor', 'F:\\dotcraft')
    })
    expect(settingsSet).not.toHaveBeenCalled()
  })

  it('switching default updates primary button aria-label', async () => {
    renderButton()

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Open' })).toBeEnabled()
    })
    fireEvent.click(screen.getByRole('button', { name: 'Choose how to open workspace' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Cursor' }))

    await waitFor(() => {
      expect(settingsSet).toHaveBeenCalledWith({ lastOpenEditorId: 'cursor' })
    })
    expect(screen.getByRole('button', { name: 'Open in Cursor' })).toBeInTheDocument()
  })

  it('does not open dropdown on chevron right click interactions', async () => {
    renderButton()

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Choose how to open workspace' })).toBeEnabled()
    })
    const chevronButton = screen.getByRole('button', { name: 'Choose how to open workspace' })
    fireEvent.contextMenu(chevronButton)
    fireEvent.mouseDown(chevronButton, { button: 2 })

    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(shellLaunchEditor).not.toHaveBeenCalled()
  })
})
