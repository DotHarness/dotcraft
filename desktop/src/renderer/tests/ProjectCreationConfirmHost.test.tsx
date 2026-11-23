import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { ProjectCreationConfirmRequest } from '../../shared/projectCreationConfirm'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ProjectCreationConfirmHost } from '../components/projects/ProjectCreationConfirmHost'
import { installDesktopApiMock } from './desktopApiMock'

function setupHost() {
  let onRequest: ((request: ProjectCreationConfirmRequest) => void) | null = null
  let onDismissed: ((payload: { requestId: string }) => void) | null = null
  const respond = vi.fn().mockResolvedValue(true)

  installDesktopApiMock({
    settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
    workspace: {
      onProjectCreationConfirmRequest: (callback) => {
        onRequest = callback
        return () => {
          onRequest = null
        }
      },
      onProjectCreationConfirmDismissed: (callback) => {
        onDismissed = callback
        return () => {
          onDismissed = null
        }
      },
      respondProjectCreationConfirm: respond
    }
  })

  render(
    <LocaleProvider>
      <ProjectCreationConfirmHost />
    </LocaleProvider>
  )

  return {
    emitRequest: (request: ProjectCreationConfirmRequest) => {
      act(() => {
        onRequest?.(request)
      })
    },
    emitDismissed: (requestId: string) => {
      act(() => {
        onDismissed?.({ requestId })
      })
    },
    respond
  }
}

describe('ProjectCreationConfirmHost', () => {
  it('shows the requested project and folders and sends confirmed: true on Create project', async () => {
    const { emitRequest, respond } = setupHost()

    emitRequest({
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
      requestId: 'req-2',
      name: 'Scratch',
      folders: ['/Users/me/Documents/Scratch'],
      createsNewFolder: true
    })

    fireEvent.keyDown(document, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(respond).toHaveBeenCalledWith({ requestId: 'req-2', confirmed: false })

    emitRequest({
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
})
