import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { DesktopToolConfirmRequest } from '../../shared/desktopToolConfirm'
import { LocaleProvider } from '../contexts/LocaleContext'
import { DesktopToolConfirmHost } from '../components/DesktopToolConfirmHost'
import { installDesktopApiMock } from './desktopApiMock'

function setupHost() {
  let onRequest: ((request: DesktopToolConfirmRequest) => void) | null = null
  let onDismissed: ((payload: { requestId: string }) => void) | null = null
  const respond = vi.fn().mockResolvedValue(true)
  const sendRequest = vi.fn()

  installDesktopApiMock({
    settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
    workspace: {
      onDesktopToolConfirmRequest: (callback) => {
        onRequest = callback
        return () => {
          onRequest = null
        }
      },
      onDesktopToolConfirmDismissed: (callback) => {
        onDismissed = callback
        return () => {
          onDismissed = null
        }
      },
      respondDesktopToolConfirm: respond
    },
    appServer: { sendRequest }
  })

  render(
    <LocaleProvider>
      <DesktopToolConfirmHost />
    </LocaleProvider>
  )

  return {
    emitRequest: (request: DesktopToolConfirmRequest) => {
      act(() => {
        onRequest?.(request)
      })
    },
    emitDismissed: (requestId: string) => {
      act(() => {
        onDismissed?.({ requestId })
      })
    },
    respond,
    sendRequest
  }
}

describe('DesktopToolConfirmHost', () => {
  it('shows the requested project and folders and sends confirmed: true on Create project', async () => {
    const { emitRequest, respond } = setupHost()

    emitRequest({
      kind: 'projectCreation',
      requestId: 'req-1',
      name: 'Workspace',
      folders: ['/work/main', '/work/shared'],
      createsNewFolder: false
    })

    expect(screen.getByRole('dialog')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Create project' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(respond).toHaveBeenCalledWith({ requestId: 'req-1', confirmed: true })
  })

  it('sends confirmed: false on Cancel or Escape and closes on main-process dismissal', async () => {
    const { emitRequest, emitDismissed, respond } = setupHost()

    emitRequest({
      kind: 'projectCreation',
      requestId: 'req-2',
      name: 'Scratch',
      folders: ['/Users/me/Documents/Scratch'],
      createsNewFolder: true
    })

    fireEvent.keyDown(document, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(respond).toHaveBeenCalledWith({ requestId: 'req-2', confirmed: false })

    emitRequest({
      kind: 'projectCreation',
      requestId: 'req-3',
      name: 'Abandoned',
      folders: ['/Users/me/Documents/Abandoned'],
      createsNewFolder: true
    })
    expect(screen.getByRole('dialog')).toBeTruthy()

    emitDismissed('req-3')
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(respond).toHaveBeenCalledTimes(1)
  })

  it('shows the plugin from the activation request and sends confirmed: true on Activate', async () => {
    const { emitRequest, respond, sendRequest } = setupHost()
    const plugin = { id: 'sample', key: 'sample', displayName: 'Sample', installed: true, enabled: false, installable: false, removable: true, source: 'workspace', rootPath: '/work/.craft/plugins/sample' }

    emitRequest({ kind: 'pluginActivation', requestId: 'req-4', plugin })

    fireEvent.click(await screen.findByRole('button', { name: 'Activate' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(respond).toHaveBeenCalledWith({ requestId: 'req-4', confirmed: true })
    expect(sendRequest).not.toHaveBeenCalled()
  })
})
