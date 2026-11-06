import { afterAll, afterEach, expect, it, vi } from 'vitest'
import { EventEmitter } from 'node:events'
import { createReplWorkerFixture } from './helpers/replWorkerFixture'

vi.mock('electron', () => ({ app: { getAppPath: () => process.cwd() }, BrowserWindow: { getAllWindows: () => [] },
  session: { fromPartition: vi.fn() }, shell: {}, nativeImage: {} }))

import { BrowserUseManager } from '../browserUseManager'
import { NodeReplManager } from '../nodeReplManager'

const workers = createReplWorkerFixture()
afterAll(() => workers.dispose())
let browser: BrowserUseManager
let repl: NodeReplManager
afterEach(async () => { await repl?.disposeAll(); await browser?.closeBackendForTests() })

it('lists suspended user tabs without waking them and restores the page before claiming it', async () => {
  const page = Object.assign(new EventEmitter(), {
    isDestroyed: () => false, getURL: () => 'https://example.test/', getTitle: () => 'Saved',
    isLoading: () => false, stop: () => {},
    debugger: Object.assign(new EventEmitter(), {
      isAttached: () => false, attach: () => {}, detach: () => {}, sendCommand: async () => ({})
    })
  }) as unknown as Electron.WebContents
  let live = false
  const restore = vi.fn(async () => { live = true; return page })
  const state = { tabId: 'saved-page', currentUrl: 'https://example.test/', title: 'Saved', loading: false }
  const owner = Object.assign(new EventEmitter(), { id: 101, isDestroyed: () => false,
    webContents: { id: 101, isDestroyed: () => false, send: () => {} } }) as unknown as Electron.BrowserWindow
  browser = new BrowserUseManager({
    setCaptureSurface: () => {}, createAutomationTab: vi.fn(),
    getTabWebContents: () => live ? page : null, ensureTabWebContents: restore,
    listAutomationTargetTabs: () => [state], snapshotState: () => state,
    destroyTab: vi.fn(), loadAutomationUrl: vi.fn(), setAutomationState: vi.fn(), moveMouse: async () => {}
  } as any)
  repl = new NodeReplManager(browser, workers.fork)
  const evaluate = async (code: string) => {
    const result = await repl.evaluate(owner, { threadId: 'suspended-claim', workspacePath: process.cwd(), code,
      browserSession: { sessionId: 'suspended-claim', threadId: 'suspended-claim', turnId: 'turn', backendId: 'iab' } })
    expect(result.error).toBeUndefined()
    return result.resultText
  }
  const count = await evaluate(`const { setupBrowserRuntime } = await import(dotcraft.browserClientPath);
    const agent = await setupBrowserRuntime(); const browser = await agent.browsers.get('iab');
    const tabs = await browser.user.openTabs(); tabs.length;`)
  expect(count).toBe('1')
  expect(restore).not.toHaveBeenCalled()
  await evaluate('const claimed = await browser.user.claimTab(tabs[0]); true')
  expect(restore).toHaveBeenCalledOnce()
}, 30000)
