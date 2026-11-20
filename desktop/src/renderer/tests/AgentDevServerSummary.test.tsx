import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises'
import { createServer } from 'node:net'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, expect, it, vi } from 'vitest'
import { devServerManager } from '../../main/devServers/devServerManager'
import { handleDesktopRuntimeThreadToolCall, type AppServerRequestClient } from '../../main/desktopRuntimeThreadTools'
import { SummaryPanel } from '../components/conversation/summary/SummaryPanel'
import { desktopSummaryServices, type SummaryServices } from '../components/conversation/summary/summaryServices'
import { disposeSummaryFeeds } from '../components/conversation/summary/store'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConversationStore } from '../stores/conversationStore'
import { installDesktopApiMock } from './desktopApiMock'
import type { DevServerScope } from '../../shared/devServers'

const threadId = 'agent-dev-server-summary'
let fixture: string | undefined

afterEach(async () => {
  cleanup()
  disposeSummaryFeeds()
  useConversationStore.getState().setWorkspacePath('')
  await devServerManager.forgetThread(threadId)
  if (fixture) await rm(fixture, { recursive: true, force: true })
})

it('shows an agent-started server from a secondary root in Servers, not Background tasks', async () => {
  fixture = await mkdtemp(join(tmpdir(), 'dotcraft-agent-preview-'))
  const primary = join(fixture, 'primary')
  const secondary = join(fixture, 'secondary')
  const port = await new Promise<number>((resolve, reject) => {
    const listener = createServer()
    listener.once('error', reject)
    listener.listen(0, '127.0.0.1', () => {
      const address = listener.address() as { port: number }
      listener.close(error => error ? reject(error) : resolve(address.port))
    })
  })
  await mkdir(primary)
  await mkdir(join(secondary, '.craft'), { recursive: true })
  await writeFile(join(secondary, 'preview.cjs'), `
    require('node:http').createServer((_request, response) => response.end('Preview fixture'))
      .listen(Number(process.env.PORT), '127.0.0.1')
  `)
  await writeFile(join(secondary, '.craft', 'launch.json'), JSON.stringify({
    configurations: [{ name: 'Web preview', runtimeExecutable: process.execPath, program: 'preview.cjs', port }]
  }))

  const request = vi.fn(async (method: string) => {
    if (method === 'thread/read') return { thread: {
      id: threadId, cwd: primary, effectiveWorkspacePath: primary, runtimeWorkspaceRoots: [primary, secondary],
      configuration: { mode: 'agent' }
    } }
    if (method === 'thread/turns/list') return { data: [] }
    if (method === 'terminal/list') return { terminals: [] }
    return {}
  })
  const services: SummaryServices = {
    ...desktopSummaryServices,
    appServer: {
      request: request as SummaryServices['appServer']['request'],
      connectionKey: () => 'fixture-connection',
      onConnectionChange: () => () => {},
      onNotification: () => () => {}
    }
  }
  const list = vi.fn((scope: DevServerScope) => devServerManager.list(scope))
  installDesktopApiMock({
    settings: { get: async () => ({ environmentSetupTipDismissed: true }) },
    devServers: {
      list,
      watch: (_folder, listener) => {
        devServerManager.on('change', listener)
        return () => { devServerManager.off('change', listener) }
      }
    }
  })
  useConversationStore.setState({ workspacePath: primary, remoteWorkspaceActive: false })
  render(<LocaleProvider loadSettings={false}>
    <SummaryPanel services={services} threadId={threadId} workspacePath={null} busy={false} variant="popover" />
  </LocaleProvider>)
  await waitFor(() => expect(list).toHaveBeenCalledWith({ folder: primary, threadId }))
  expect(screen.queryByRole('region', { name: 'Servers' })).toBeNull()

  const client: AppServerRequestClient = { sendRequest: request as AppServerRequestClient['sendRequest'] }
  const openPreview = vi.fn()
  const start = (folder: string) => handleDesktopRuntimeThreadToolCall(client, {
    namespace: 'desktop', tool: 'StartServer', threadId, arguments: { name: 'Web preview', folder }
  }, primary, { openDevServerPreview: openPreview })
  expect(await start(fixture)).toMatchObject({ success: false, errorCode: 'DevServerFolderNotAllowed' })
  await act(async () => {
    expect(await start(secondary)).toMatchObject({ success: true })
  })
  await waitFor(() => expect(devServerManager.servers({ folder: primary, threadId })[0]?.status).toBe('running'), { timeout: 4_000 })
  const started = devServerManager.servers({ folder: primary, threadId })[0]
  expect(started.folder).toBe(secondary)
  expect(openPreview).toHaveBeenCalledWith({ threadId, name: 'Web preview', url: started.url })
  const servers = await screen.findByRole('region', { name: 'Servers' })
  expect(within(servers).getByRole('button', { name: /Web preview/ })).toBeTruthy()
  expect(screen.queryByRole('region', { name: 'Background tasks' })).toBeNull()
}, 12_000)
