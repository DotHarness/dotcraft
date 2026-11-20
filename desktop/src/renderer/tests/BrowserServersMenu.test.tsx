import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { DevServerList } from '../../shared/devServers'
import { BrowserServersMenu } from '../components/detail/viewers/BrowserServersMenu'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { installDesktopApiMock } from './desktopApiMock'

const THREAD_ID = 'thread-1'
const SERVER_URL = 'http://localhost:5173'
const start = vi.fn(async () => ({ ok: false as const, failure: { kind: 'launchFile' as const, message: '' } }))

function show(platform: 'linux' | 'win32', folder: string, serverFolder: string): void {
  installDesktopApiMock({ platform, devServers: { start } })
  const list: DevServerList = {
    folder,
    launch: { status: 'ok', path: `${folder}/.craft/launch.json`, configurations: [{ name: 'web', port: 5173 }], skipped: [] },
    servers: [{
      serverId: 'project-web', name: 'web', threadId: THREAD_ID, folder: serverFolder, port: 5173,
      url: SERVER_URL, status: 'running', startedAt: '2026-11-20T00:00:00.000Z'
    }]
  }
  render(
    <LocaleProvider loadSettings={false}>
      <BrowserServersMenu scope={{ folder, threadId: THREAD_ID }} list={list} boundServer={null} logsOpen={false} onToggleLogs={() => undefined} />
    </LocaleProvider>
  )
}

function openMenu(): void {
  fireEvent.click(screen.getByRole('button', { name: 'Servers' }))
}

function expectRunningServerOpened(): void {
  expect(useDetailPanelStore.getState().getThreadState(THREAD_ID).tabs).toEqual([
    expect.objectContaining({ kind: 'browser', currentUrl: SERVER_URL })
  ])
}

afterEach(() => {
  cleanup()
  useDetailPanelStore.getState().reset()
  vi.clearAllMocks()
})

describe('browser Servers menu', () => {
  it('opens the existing server when Windows spells the launch folder with different casing', () => {
    show('win32', 'c:\\work\\app', 'C:\\Work\\App')
    openMenu()

    fireEvent.click(screen.getByRole('menuitem', { name: 'web' }))
    expectRunningServerOpened()
    expect(start).not.toHaveBeenCalled()
  })

  it.each([
    { scenario: 'a different worktree', platform: 'win32', folder: 'C:\\Work\\App-worktree', serverFolder: 'C:\\Work\\App' },
    { scenario: 'a folder distinguished by a leading space', platform: 'win32', folder: 'C:\\Work\\ App', serverFolder: 'C:\\Work\\App' },
    { scenario: 'a POSIX folder distinguished by casing', platform: 'linux', folder: '/work/app', serverFolder: '/work/App' }
  ] as const)('keeps the current configuration runnable beside a server from $scenario', ({ platform, folder, serverFolder }) => {
    show(platform, folder, serverFolder)
    openMenu()

    const rows = screen.getAllByRole('menuitem', { name: 'web' })
    expect(rows).toHaveLength(2)
    fireEvent.click(rows[0])
    openMenu()
    fireEvent.click(screen.getAllByRole('menuitem', { name: 'web' })[1])

    expect(start).toHaveBeenCalledExactlyOnceWith({ folder, threadId: THREAD_ID }, 'web')
    expectRunningServerOpened()
  })
})
