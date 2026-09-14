import { afterAll, afterEach, expect, it, vi } from 'vitest'
import { createMockBridge } from './helpers/chromeBridgeFixture'
import { createReplWorkerFixture } from './helpers/replWorkerFixture'
import { createFakeBrowserManager } from './helpers/replBrowserFixture'
vi.mock('electron', () => ({ app: { getAppPath: () => process.cwd() }, BrowserWindow: vi.fn() }))
import { NodeReplManager } from '../nodeReplManager'

const fixture = createReplWorkerFixture()
const backend = createFakeBrowserManager()
const manager = new NodeReplManager(backend as never, fixture.fork)
const bridges: ReturnType<typeof createMockBridge>[] = []
const evaluate = (code: string, evaluationId = 'call') => manager.evaluate({} as Electron.BrowserWindow, {
  threadId: 'chrome-worker', turnId: 'turn', evaluationId, code
})
afterEach(async () => {
  await manager.disposeAll()
  await Promise.all(bridges.splice(0).map(bridge => bridge.close()))
})
afterAll(async () => { await backend.closeBackendForTests(); fixture.dispose() })

it('reuses Chrome handles and fresh metadata through the worker, preserves errors and cancels pending commands', async () => {
  const tab = { id: 7, tabId: 7, windowId: 1, title: 'Example', url: 'https://example.test/' }
  const bridge = createMockBridge(request => {
    if (request.method === 'tabs.selected') return tab
    if (request.method === 'tab.title') return tab.title
    if (request.method === 'tab.reload') throw new Error('ordinary command failure')
    if (request.method === 'tab.evaluate') return new Promise(() => {})
    throw new Error(`Unexpected command: ${request.method}`)
  })
  bridges.push(bridge)
  const pipePath = await bridge.listen()
  const initialized = await evaluate(`
    const { setupBrowserRuntime } = await import(dotcraft.chromeBrowserClientPath);
    const agent = await setupBrowserRuntime({ chromeHost: { pipePaths: [${JSON.stringify(pipePath)}] } });
    const browser = await agent.browsers.get('extension');
    let tab = await browser.tabs.selected();
    nodeRepl.write(await browser.documentation());
    await tab.title();
  `, 'init')
  expect(initialized.error).toBeUndefined()
  expect(initialized.resultText).toBe('Example')
  expect(initialized.logs[0]).toContain('goto(url)')
  expect((await evaluate('await tab.reload()', 'failed')).error).toContain('ordinary command failure')
  expect((await evaluate('await tab.title()', 'reused')).resultText).toBe('Example')
  expect(bridge.requests.at(-1)?.browserSession).toMatchObject({ threadId: 'chrome-worker', evaluationId: 'reused' })
  const pending = evaluate('await tab.evaluate("return true")', 'cancelled')
  await vi.waitFor(() => expect(bridge.requests.at(-1)?.method).toBe('tab.evaluate'))
  expect(manager.cancel('chrome-worker', 'cancelled').ok).toBe(true)
  expect((await pending).error).toContain('cancelled')
  await vi.waitFor(() => expect(bridge.cancels).toHaveLength(1))
  expect(bridge.cancels[0].browserSession).toMatchObject({ evaluationId: 'cancelled' })
  expect((await evaluate('typeof tab', 'fresh')).resultText).toBe('undefined')
})
