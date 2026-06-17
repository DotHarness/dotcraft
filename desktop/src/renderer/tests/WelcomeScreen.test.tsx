import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { WelcomeScreen } from '../components/WelcomeScreen'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const workspaceGetRecent = vi.fn()
const workspacePickFolder = vi.fn()
const workspaceSwitch = vi.fn()
const onOpenWorkspace = vi.fn()

function deferredPromise(): { promise: Promise<void>; reject: (err: unknown) => void } {
  let reject!: (err: unknown) => void
  const promise = new Promise<void>((_resolve, rej) => {
    reject = rej
  })
  return { promise, reject }
}

function renderWelcome() {
  return render(
    <LocaleProvider>
      <WelcomeScreen onOpenWorkspace={onOpenWorkspace} />
    </LocaleProvider>
  )
}

describe('WelcomeScreen', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    settingsSet.mockResolvedValue(undefined)
    workspaceGetRecent.mockResolvedValue([])
    workspacePickFolder.mockResolvedValue(null)
    workspaceSwitch.mockResolvedValue(undefined)
    onOpenWorkspace.mockResolvedValue(undefined)
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({
      left: 120,
      top: 80,
      width: 96,
      height: 96,
      right: 216,
      bottom: 176,
      x: 120,
      y: 80,
      toJSON: () => {}
    } as DOMRect)

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        platform: 'win32',
        titleBarOverlayHeight: 32,
        settings: {
          get: settingsGet,
          set: settingsSet
        },
        workspace: {
          getRecent: workspaceGetRecent,
          pickFolder: workspacePickFolder,
          switch: workspaceSwitch
        }
      }
    })
  })

  it('plays the brand opening state before switching a picked workspace', async () => {
    const opening = new Promise<void>(() => {})
    workspacePickFolder.mockResolvedValue('F:\\Git\\site')
    onOpenWorkspace.mockReturnValue(opening)
    renderWelcome()

    const openWorkspaceRow = screen.getByRole('button', { name: 'Open Workspace' })
    fireEvent.click(openWorkspaceRow)

    await act(async () => {
      await Promise.resolve()
    })

    expect(screen.getByRole('button', { name: 'Open Workspace' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'EN' })).toBeDisabled()
    expect(onOpenWorkspace).toHaveBeenCalledWith({
      path: 'F:\\Git\\site',
      logoRect: {
        left: 120,
        top: 80,
        width: 96,
        height: 96
      }
    })
    expect(workspaceSwitch).not.toHaveBeenCalled()
  })

  it('uses the same brand opening state for recent workspaces', async () => {
    workspaceGetRecent.mockResolvedValue([
      { path: 'F:\\dotcraft', name: 'dotcraft', lastOpenedAt: '2026-05-16T00:00:00.000Z' }
    ])
    renderWelcome()

    await act(async () => {
      await Promise.resolve()
    })

    const recentWorkspace = screen.getByRole('button', { name: 'Open workspace dotcraft' })
    fireEvent.click(recentWorkspace)

    await act(async () => {
      await Promise.resolve()
    })

    expect(recentWorkspace).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Open Workspace' })).toBeDisabled()
    expect(onOpenWorkspace).toHaveBeenCalledWith({
      path: 'F:\\dotcraft',
      logoRect: {
        left: 120,
        top: 80,
        width: 96,
        height: 96
      }
    })
    expect(workspaceSwitch).not.toHaveBeenCalled()
  })

  it('restores interaction and shows the existing error when switching fails', async () => {
    const opening = deferredPromise()
    workspacePickFolder.mockResolvedValue('F:\\broken')
    onOpenWorkspace.mockReturnValue(opening.promise)
    renderWelcome()

    fireEvent.click(screen.getByRole('button', { name: 'Open Workspace' }))

    await act(async () => {
      await Promise.resolve()
    })
    expect(screen.getByRole('button', { name: 'Open Workspace' })).toBeDisabled()

    await act(async () => {
      opening.reject(new Error('switch failed'))
      await Promise.resolve()
      await Promise.resolve()
    })

    expect(screen.getByText('switch failed')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open Workspace' })).not.toBeDisabled()
    expect(screen.getByRole('button', { name: 'EN' })).not.toBeDisabled()
    expect(workspaceSwitch).not.toHaveBeenCalled()
  })
})
