import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DevServerList } from '../../shared/devServers'
import { BrowserServersMenu } from '../components/detail/viewers/BrowserServersMenu'
import { LocaleProvider } from '../contexts/LocaleContext'

const WORKTREE = '/work/app-worktree'
const PROJECT = '/work/app'

const start = vi.fn(async () => ({ ok: false as const, failure: { kind: 'launchFile' as const, message: '' } }))

beforeEach(() => {
  Object.assign(window, { api: { ...(window as unknown as { api?: object }).api, devServers: { start } } })
})

afterEach(() => {
  cleanup()
  vi.clearAllMocks()
})

describe('browser Servers menu', () => {
  it('keeps the current folder’s configuration runnable beside a server inherited from another folder', async () => {
    const list: DevServerList = {
      folder: WORKTREE,
      launch: { status: 'ok', path: `${WORKTREE}/.craft/launch.json`, configurations: [{ name: 'web', port: 5173 }], skipped: [] },
      servers: [{
        serverId: 'project-web', name: 'web', threadId: 'thread-1', folder: PROJECT, port: 5173,
        url: 'http://localhost:5173', status: 'running', startedAt: '2026-11-20T00:00:00.000Z'
      }]
    }
    render(
      <LocaleProvider loadSettings={false}>
        <BrowserServersMenu scope={{ folder: WORKTREE, threadId: 'thread-1' }} list={list} boundServer={null} logsOpen={false} onToggleLogs={() => undefined} />
      </LocaleProvider>
    )
    fireEvent.click(screen.getByRole('button', { name: 'Servers' }))
    const rows = screen.getAllByRole('menuitem', { name: /web/ })
    expect(rows).toHaveLength(2)
    fireEvent.click(rows[0])
    expect(start).toHaveBeenCalledWith({ folder: WORKTREE, threadId: 'thread-1' }, 'web')
  })
})
