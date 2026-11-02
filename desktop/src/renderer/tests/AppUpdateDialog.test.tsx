import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { AppUpdateState } from '../../shared/appUpdate'
import { AppUpdateDialog } from '../components/update/AppUpdateDialog'
import { LocaleProvider } from '../contexts/LocaleContext'

const update = { latestVersion: '0.9.11', htmlUrl: 'https://example.test/releases/0.9.11' }

function fixture(state: Partial<AppUpdateState>) {
  const onInstall = vi.fn()
  const onDownload = vi.fn()
  const onClose = vi.fn()
  render(
    <LocaleProvider loadSettings={false}>
      <AppUpdateDialog
        state={{ status: 'downloaded', currentVersion: '0.9.10', update, ...state }}
        onClose={onClose} onInstall={onInstall} onDownload={onDownload}
      />
    </LocaleProvider>
  )
  return { onInstall, onDownload, onClose }
}

describe('AppUpdateDialog actions', () => {
  it('disables installation while preparation is in progress', () => {
    const { onInstall, onDownload } = fixture({ status: 'preparing' })
    const busy = screen.getAllByRole('button').find((button) => button.getAttribute('aria-busy') === 'true')!
    expect(busy).toBeDisabled()
    fireEvent.click(busy)
    expect(onInstall).not.toHaveBeenCalled()
    expect(onDownload).not.toHaveBeenCalled()
  })

  it('retries installation instead of download after preparation or installer-launch failure', () => {
    const { onInstall, onDownload } = fixture({ status: 'error', retryAction: 'install', error: 'Process still running.' })
    const primary = screen.getByRole('button', { name: 'Retry installation' })
    fireEvent.click(primary)
    expect(onInstall).toHaveBeenCalledOnce()
    expect(onDownload).not.toHaveBeenCalled()
  })

  it('keeps download retry for failures without a downloaded installer', () => {
    const { onInstall, onDownload } = fixture({ status: 'error', retryAction: 'download', error: 'Network unavailable.' })
    fireEvent.click(screen.getByRole('button', { name: 'Retry download' }))
    expect(onDownload).toHaveBeenCalledOnce()
    expect(onInstall).not.toHaveBeenCalled()
  })
})
