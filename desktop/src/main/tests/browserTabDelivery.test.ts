import { createReplWorkerFixture } from './helpers/replWorkerFixture'
import { afterAll, afterEach, expect, it, vi } from 'vitest'
import { EventEmitter } from 'node:events'

vi.mock('electron', () => ({
  app: { getAppPath: () => process.cwd() }, BrowserWindow: { getAllWindows: () => [] },
  WebContentsView: vi.fn(), session: { fromPartition: vi.fn() }, shell: {}, nativeImage: {}
}))

import { BrowserUseManager } from '../browserUseManager'
import { NodeReplManager } from '../nodeReplManager'

const workerFixture = createReplWorkerFixture()

let manager: BrowserUseManager
let repl: NodeReplManager
afterAll(() => workerFixture.dispose())

afterEach(async () => {
  await repl?.disposeAll()
  await manager?.closeBackendForTests()
})

it('delivers multiple live pages, releases client handles and reclaims them without reload or close', async () => {
  const pages = new Map<string, Electron.WebContents>()
  const createPage = () => Object.assign(new EventEmitter(), {
    isDestroyed: () => false, getURL: () => 'about:blank', getTitle: () => 'Delivered page',
    isLoading: () => false, stop: () => {},
    debugger: Object.assign(new EventEmitter(), {
      isAttached: () => false, attach: () => {}, detach: vi.fn(), sendCommand: async () => ({})
    })
  }) as unknown as Electron.WebContents
  const snapshot = (tabId: string) => ({ tabId, currentUrl: 'about:blank', title: 'Delivered page', loading: false })
  const destroy = vi.fn((_owner: unknown, id: string) => { pages.delete(id) })
  const load = vi.fn()
  const send = vi.fn()
  const owner = Object.assign(new EventEmitter(), {
    id: 101, isDestroyed: () => false, getTitle: () => 'Delivery fixture',
    webContents: { isDestroyed: () => false, send }
  }) as unknown as Electron.BrowserWindow
  manager = new BrowserUseManager({
    createAutomationTab: (_owner: unknown, params: { tabId: string }) => { pages.set(params.tabId, createPage()) },
    getTabWebContents: (_owner: unknown, id: string) => pages.get(id) ?? null,
    listAutomationTargetTabs: () => [...pages.keys()].map(snapshot),
    snapshotState: (_owner: unknown, id: string) => snapshot(id),
    destroyTab: destroy, loadAutomationUrl: load, setAutomationState: () => {},
    clickMouse: async () => {}, doubleClickMouse: async () => {}, moveMouse: async () => {},
    typeText: async () => {}, keypress: () => {}, scrollMouse: () => {}, dragMouse: async () => {}
  } as any)
  repl = new NodeReplManager(manager, workerFixture.fork)
  let turnId = 'one'
  const evaluate = async (code: string) => {
    const result = await repl.evaluate(owner, {
      threadId: 'delivery', workspacePath: process.cwd(), code,
      browserSession: { sessionId: 'delivery', threadId: 'delivery', turnId, backendId: 'iab' }
    })
    expect(result.error).toBeUndefined()
    return result.resultText ? JSON.parse(result.resultText) : undefined
  }
  const finish = (id = turnId, status = 'completed') => manager.handleTurnNotification(
    `turn/${status}`, { threadId: 'delivery', turn: { id } })
  await evaluate(`
    const { setupBrowserRuntime } = await import(dotcraft.browserClientPath);
    const agent = await setupBrowserRuntime();
    const browser = await agent.browsers.get('iab');
    const first = await browser.tabs.new();
    const second = await browser.tabs.new();
    await first.markDeliverable(); await second.markDeliverable();
    true;
  `)
  const original = [...pages.values()]
  load.mockClear()
  finish()
  finish()
  expect(pages.size).toBe(2)
  for (const page of original) expect(page.listenerCount('console-message')).toBe(0)
  expect(await evaluate('await browser.tabs.list()')).toEqual([])
  expect(await evaluate('let released = false; try { await first.reload() } catch { released = true }; released')).toBe(true)
  turnId = 'chat'
  manager.handleTurnNotification('turn/started', { threadId: 'delivery', turn: { id: turnId } })
  await evaluate('42')
  finish()
  turnId = 'two'
  const users = await evaluate('await browser.user.openTabs()')
  expect(users).toHaveLength(2)
  await evaluate('let claimed = await browser.user.claimTab((await browser.user.openTabs())[0]); true')
  expect(load).not.toHaveBeenCalled()
  expect(send.mock.calls.some(([channel]) => channel === 'viewer:browser:close')).toBe(false)
  await evaluate('const scratch = await browser.tabs.new(); await scratch.markHandoff(); true')
  finish('one')
  finish('two', 'cancelled')
  turnId = 'chat-after-handoff'
  manager.handleTurnNotification('turn/started', { threadId: 'delivery', turn: { id: turnId } })
  await evaluate('42')
  finish()
  expect(pages.size).toBe(3)
  expect(destroy).not.toHaveBeenCalled()
  turnId = 'next-browser-use'
  await evaluate('await browser.tabs.list()')
  finish(turnId, 'failed')
  expect(destroy).toHaveBeenCalledTimes(1)
  load.mockClear()
  send.mockClear()
  repl.reset('delivery')
  expect(await evaluate('JSON.stringify(typeof browser)')).toBe('undefined')
  expect([...pages.values()]).toEqual(original)
  expect(destroy).toHaveBeenCalledTimes(1)
  expect(load).not.toHaveBeenCalled()
  expect(send.mock.calls.some(([channel]) => channel === 'viewer:browser:close')).toBe(false)
}, 30000)
