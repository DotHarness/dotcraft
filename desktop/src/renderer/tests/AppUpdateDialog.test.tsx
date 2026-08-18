// @vitest-environment jsdom
import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { AppUpdateInfo, AppUpdateState } from '../../shared/appUpdate'
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
  it('renders release notes through the shared markdown renderer', () => {
    renderDialog({
      releaseNotes: [
        '# DotCraft v0.1.9',
        '',
        '1. **Docker Deployment**',
        '',
        '[View release](https://example.com/release)'
      ].join('\n')
    })

    expect(screen.getByRole('heading', { name: 'DotCraft v0.1.9', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('listitem')).toHaveTextContent('Docker Deployment')
    expect(document.querySelector('strong')).toHaveTextContent('Docker Deployment')
    expect(document.body.textContent).not.toContain('**Docker Deployment**')

    fireEvent.click(screen.getByRole('link', { name: /view release/i }))
    expect(openExternal).toHaveBeenCalledWith('https://example.com/release')
  })
})

function renderDialog(update?: Partial<AppUpdateInfo>): void {
  const state: AppUpdateState = {
    status: 'available',
    currentVersion: '0.1.8',
    update: {
      currentVersion: '0.1.8',
      latestVersion: '0.1.9',
      tagName: 'v0.1.9',
      assetName: 'DotCraft-v0.1.9-win-x64-Setup.exe',
      sizeBytes: 192 * 1024 * 1024,
      downloadUrl: 'https://github.com/DotHarness/dotcraft/releases/download/v0.1.9/DotCraft-v0.1.9-win-x64-Setup.exe',
      htmlUrl: 'https://github.com/DotHarness/dotcraft/releases/tag/v0.1.9',
      ...update
    }
  }

  render(
    <LocaleProvider>
      <AppUpdateDialog
        state={state}
        onClose={vi.fn()}
        onDownload={vi.fn()}
      />
    </LocaleProvider>
  )
}
