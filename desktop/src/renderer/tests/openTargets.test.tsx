import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { OpenTargetButton } from '../components/conversation/OpenTargetButton'
import { ThreadHeader } from '../components/conversation/ThreadHeader'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useOpenTargetsStore } from '../stores/openTargetsStore'
import { useThreadStore } from '../stores/threadStore'
import type { OpenTargetPreferences } from '../../shared/openTargets'
import { installDesktopApiMock } from './desktopApiMock'

const ROOT = 'C:\\repo'
const listOpenTargets = vi.fn()
const openInTarget = vi.fn()
const settingsGet = vi.fn()
const settingsSet = vi.fn()

const TARGETS = [
  { id: 'vscode', label: 'VS Code', iconKey: 'editor' },
  { id: 'fileManager', label: 'File Explorer', labelKey: 'openTargets.fileManager', iconKey: 'fileManager' },
  { id: 'rider', label: 'Rider', iconKey: 'editor' },
  { id: 'systemDefault', label: 'Default app', iconKey: 'fileManager', hidden: true }
]

function preferences(value: OpenTargetPreferences | undefined): void {
  settingsGet.mockResolvedValue({ locale: 'en', openTargetPreferences: value })
}

function renderButton(rememberChoice: boolean): void {
  render(
    <LocaleProvider>
      <OpenTargetButton
        targetPath={`${ROOT}\\src\\app.ts`}
        projectRoot={ROOT}
        rememberChoice={rememberChoice}
        tooltipLabel="Open file"
        menuAriaLabel="Open options"
      />
    </LocaleProvider>
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  useOpenTargetsStore.setState({ status: 'idle', targets: [], preferences: undefined })
  listOpenTargets.mockResolvedValue(TARGETS)
  openInTarget.mockResolvedValue(undefined)
  settingsSet.mockResolvedValue(undefined)
  preferences(undefined)
  installDesktopApiMock({
    settings: { get: settingsGet, set: settingsSet },
    shell: { listOpenTargets, openInTarget },
    appServer: { onNotification: vi.fn(() => () => {}), sendRequest: vi.fn().mockResolvedValue({}) }
  })
})

describe('effective open target', () => {
  it('prefers the project choice, then the global choice, then the first installed app', async () => {
    preferences({ global: 'rider', perProject: { [ROOT]: 'fileManager', 'C:\\other': 'vscode' } })
    renderButton(false)
    fireEvent.click(await screen.findByRole('button', { name: 'Open in File Explorer' }))
    await waitFor(() => expect(openInTarget).toHaveBeenLastCalledWith({ targetId: 'fileManager', path: `${ROOT}\\src\\app.ts` }))

    act(() => useOpenTargetsStore.setState({ preferences: { global: 'rider' } }))
    fireEvent.click(screen.getByRole('button', { name: 'Open in Rider' }))
    await waitFor(() => expect(openInTarget).toHaveBeenLastCalledWith({ targetId: 'rider', path: `${ROOT}\\src\\app.ts` }))

    act(() => useOpenTargetsStore.setState({ preferences: { global: 'zed' } }))
    fireEvent.click(screen.getByRole('button', { name: 'Open in VS Code' }))
    await waitFor(() => expect(openInTarget).toHaveBeenLastCalledWith({ targetId: 'vscode', path: `${ROOT}\\src\\app.ts` }))
  })

  it('shows the load failure and retries when the menu is reopened', async () => {
    listOpenTargets.mockRejectedValueOnce(new Error('offline'))
    await act(() => useOpenTargetsStore.getState().refresh())
    expect(useOpenTargetsStore.getState().status).toBe('error')

    await act(() => useOpenTargetsStore.getState().refresh())
    expect(useOpenTargetsStore.getState().status).toBe('ready')
    expect(useOpenTargetsStore.getState().targets).toEqual(TARGETS)
  })
})

describe('remembering open targets', () => {
  it('remembers an app picked from the file viewer menu for the project and globally', async () => {
    renderButton(true)
    fireEvent.click(await screen.findByRole('button', { name: 'Open options' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Rider' }))

    await waitFor(() => expect(openInTarget).toHaveBeenCalledWith({ targetId: 'rider', path: `${ROOT}\\src\\app.ts` }))
    expect(settingsSet).toHaveBeenCalledWith({ openTargetPreferences: { global: 'rider', perProject: { [ROOT]: 'rider' } } })
  })

  it('opens without remembering when the menu does not own the default', async () => {
    renderButton(false)
    fireEvent.click(await screen.findByRole('button', { name: 'Open options' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Rider' }))

    await waitFor(() => expect(openInTarget).toHaveBeenCalledWith({ targetId: 'rider', path: `${ROOT}\\src\\app.ts` }))
    expect(settingsSet).not.toHaveBeenCalled()
  })

  it('opens the project root from the thread menu and remembers the pick', async () => {
    useConnectionStore.getState().reset()
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
    render(
      <LocaleProvider>
        <ThreadHeader threadName="Thread" threadId="thread-1" workspacePath={ROOT} />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'More chat actions' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Open in' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'VS Code' }))

    await waitFor(() => expect(openInTarget).toHaveBeenCalledWith({ targetId: 'vscode', path: ROOT }))
    expect(settingsSet).toHaveBeenCalledWith({ openTargetPreferences: { global: 'vscode', perProject: { [ROOT]: 'vscode' } } })
  })
})
