// @vitest-environment jsdom
import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { AppUpdateState } from '../../shared/appUpdate'
import { AppUpdateDialog } from '../components/update/AppUpdateDialog'
import { LocaleProvider } from '../contexts/LocaleContext'
import { installDesktopApiMock } from './desktopApiMock'

const openExternal = vi.fn()

beforeEach(() => {
  installDesktopApiMock({
    initialLocale: 'en',
    settings: {
      get: () => Promise.resolve({ locale: 'en' })
    },
    shell: {
      openExternal
    }
  })
  openExternal.mockReset()
})

describe('AppUpdateDialog', () => {
  it('renders sanitized release notes and opens their links externally', () => {
    renderDialog({
      status: 'available',
      releaseNotes: '<h1>DotCraft v0.7.4</h1><img src="x" onerror="alert(1)"><p><a href="https://example.com/release">Details</a></p>'
    })

    expect(screen.getByRole('heading', { name: 'DotCraft v0.7.4', level: 1 })).toBeInTheDocument()
    expect(document.querySelector('[onerror]')).toBeNull()

    fireEvent.click(screen.getByRole('link', { name: 'Details' }))
    expect(openExternal).toHaveBeenCalledWith('https://example.com/release')
  })

  it('installs a downloaded update from the primary action', () => {
    const { onDownload, onInstall } = renderDialog({ status: 'downloaded' })

    fireEvent.click(screen.getByRole('button', { name: 'Restart to update' }))

    expect(onInstall).toHaveBeenCalledOnce()
    expect(onDownload).not.toHaveBeenCalled()
  })
})

function renderDialog({ status, releaseNotes }: { status: AppUpdateState['status']; releaseNotes?: string }) {
  const onDownload = vi.fn()
  const onInstall = vi.fn()
  const state: AppUpdateState = {
    status,
    currentVersion: '0.7.3',
    update: {
      latestVersion: '0.7.4',
      releaseNotes,
      htmlUrl: 'https://github.com/DotHarness/dotcraft/releases/tag/v0.7.4'
    }
  }

  render(
    <LocaleProvider>
      <AppUpdateDialog state={state} onClose={vi.fn()} onDownload={onDownload} onInstall={onInstall} />
    </LocaleProvider>
  )
  return { onDownload, onInstall }
}
