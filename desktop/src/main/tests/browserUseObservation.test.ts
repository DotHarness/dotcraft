import { createReplWorkerFixture } from './helpers/replWorkerFixture'
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { EventEmitter } from 'node:events'
import { chromium, type Browser, type Page, type CDPSession } from 'playwright-core'

vi.mock('electron', () => ({
  app: { getAppPath: () => process.cwd() }, BrowserWindow: { getAllWindows: () => [] },
  WebContentsView: vi.fn(), session: { fromPartition: vi.fn() }, shell: {}, nativeImage: {}
}))

import { BrowserUseManager } from '../browserUseManager'
import { NodeReplManager } from '../nodeReplManager'

const workerFixture = createReplWorkerFixture()

describe('bundled browser client with a real page', () => {
  let browser: Browser
  let page: Page
  let cdp: CDPSession
  let manager: BrowserUseManager
  let repl: NodeReplManager
  let owner: Electron.BrowserWindow
  let currentId = ''
  let turnId = 'turn-semantic-1'

  beforeAll(async () => {
    browser = await chromium.launch({ headless: true })
    page = await browser.newPage()
    await page.setContent(`<title>Semantic fixture</title>
      <button id="save" onclick="this.dataset.clicked='yes'"><span>Save nested</span></button>
      <button hidden>Hidden button</button><fieldset disabled><button>Disabled field</button></fieldset>
      <div aria-disabled="true"><button>Disabled aria</button></div>
      <label for="name">Account name</label><input id="name">
      <span id="label">Accessible title</span><button aria-labelledby="label">Other text</button>
      <div id="shadow"></div><iframe id="frame" srcdoc="<label for='inner'>Frame name</label><input id='inner'><button onclick=&quot;this.textContent='Frame clicked'&quot;>Frame action</button>"></iframe>
      <script>document.querySelector('#shadow').attachShadow({mode:'open'}).innerHTML='<label for="s">Shadow name</label><input id="s"><button>Shadow action</button>'</script>`)
    await page.frameLocator('#frame').getByRole('button').waitFor()
    cdp = await page.context().newCDPSession(page)
    const debug = new EventEmitter()
    const events = new EventEmitter()
    const contents = Object.assign(events, {
      isDestroyed: () => page.isClosed(), getURL: () => page.url(), getTitle: () => 'Semantic fixture',
      isLoading: () => false, stop: () => {},
      executeJavaScript: (source: string) => page.evaluate(source),
      debugger: Object.assign(debug, {
        isAttached: () => true, attach: () => {}, detach: () => {},
        sendCommand: (method: string, params: any) => cdp.send(method as any, params)
      })
    })
    owner = Object.assign(new EventEmitter(), { id: 100, isDestroyed: () => false, getTitle: () => 'Test', webContents: { isDestroyed: () => false, send: () => {} } }) as unknown as Electron.BrowserWindow
    manager = new BrowserUseManager({
      createAutomationTab: (_win: unknown, params: { tabId: string }) => { currentId = params.tabId },
      getTabWebContents: () => contents as unknown as Electron.WebContents,
      loadAutomationUrl: async () => {}, destroyTab: () => {},
      snapshotState: () => ({ tabId: currentId, currentUrl: page.url(), title: 'Semantic fixture', loading: false }),
      setAutomationState: () => {},
      clickMouse: async (_win: unknown, params: any) => page.mouse.click(params.x, params.y),
      doubleClickMouse: async (_win: unknown, params: any) => page.mouse.dblclick(params.x, params.y),
      moveMouse: async (_win: unknown, params: any) => page.mouse.move(params.x, params.y),
      typeText: async (_win: unknown, params: any) => page.keyboard.insertText(params.text),
      keypress: () => {}, scrollMouse: () => {}, dragMouse: async () => {}
    } as any)
    repl = new NodeReplManager(manager, workerFixture.fork)
  }, 30000)

  afterAll(async () => {
    await repl?.disposeAll()
    workerFixture.dispose()
    await manager?.closeBackendForTests()
    await browser?.close()
  })

  async function evaluate(code: string): Promise<any> {
    const result = await repl.evaluate(owner, {
      threadId: 'semantic', workspacePath: process.cwd(), code,
      browserSession: { sessionId: 'semantic', threadId: 'semantic', turnId, backendId: 'iab' }
    })
    expect(result.error, result.error).toBeUndefined()
    return result.resultText ? JSON.parse(result.resultText) : undefined
  }

  it('observes real names and states, frames and open shadow roots', async () => {
    const snapshot = await evaluate(`
      const { setupBrowserRuntime } = await import(dotcraft.browserClientPath);
      const agent = await setupBrowserRuntime();
      const browser = await agent.browsers.get('iab');
      let tab = await browser.tabs.new();
      await tab.playwright.domSnapshot();
    `)
    expect(snapshot.elements.some((element: any) => element.name === 'Hidden button')).toBe(false)
    for (const name of ['Save nested', 'Account name', 'Accessible title', 'Frame name', 'Frame action', 'Shadow name', 'Shadow action']) {
      expect(snapshot.elements.some((element: any) => element.name === name), name).toBe(true)
    }
    expect(snapshot.elements.find((element: any) => element.name === 'Disabled field').enabled).toBe(false)
    expect(snapshot.elements.find((element: any) => element.name === 'Disabled aria').enabled).toBe(false)
    expect(snapshot.accessibilitySnapshot).toContain('Accessible title')
  })

  it('uses the same semantics for locator actions and fresh opaque DOM-CUA ids', async () => {
    await evaluate(`
      await tab.playwright.getByLabel('Account name').fill('Ada');
      await tab.playwright.getByLabel('Shadow name').fill('Grace');
      await tab.playwright.frameLocator('#frame').getByLabel('Frame name').fill('Linus');
      await tab.playwright.frameLocator('#frame').getByRole('button', { name: 'Frame action' }).click();
      const nodes = JSON.parse(await tab.playwright.domSnapshot()).elements;
      await tab.dom_cua.click({ node_id: nodes.find(node => node.name === 'Save nested').ref });
      true;
    `)
    expect(await page.locator('#name').inputValue()).toBe('Ada')
    expect(await page.getByLabel('Shadow name').inputValue()).toBe('Grace')
    expect(await page.frameLocator('#frame').getByLabel('Frame name').inputValue()).toBe('Linus')
    expect(await page.frameLocator('#frame').getByRole('button').innerText()).toBe('Frame clicked')
    expect(await page.locator('#save').getAttribute('data-clicked')).toBe('yes')
  })

  it('publishes only supported documentation and keeps page tools dynamically gated', async () => {
    const result = await evaluate(`
      const documentation = await browser.documentation();
      const tabsTopic = await agent.documentation.get('tabs');
      const webmcpTopic = await agent.documentation.get('webmcp');
      const capabilities = await tab.capabilities.list();
      let unknown = '';
      try { await agent.documentation.get('browserAuth'); } catch (error) { unknown = String(error); }
      ({ documentation, tabsTopic, webmcpTopic, capabilities, unknown });
    `)
    expect(result.documentation).toContain('markDeliverable')
    expect(result.documentation).toContain('agent.documentation.get("viewport")')
    expect(result.tabsTopic).toContain('active turn')
    expect(result.webmcpTopic).toContain('Navigation can change availability')
    expect(result.capabilities.map((item: any) => item.id)).not.toContain('webmcp')
    expect(result.unknown).toContain('Available topics:')
    expect(result.documentation).not.toContain('browserAuth.request')
  })

  it('refreshes opaque ids and reports disabled/hidden state through the client', async () => {
    const result = await evaluate(String.raw`
      const old = JSON.parse(await tab.playwright.domSnapshot()).elements.find(node => node.name === 'Save nested').ref;
      const visible = await tab.dom_cua.get_visible_dom();
      const fresh = visible.split('\n').find(line => line.includes('Save nested')).match(/node_id=([^ ]+)/)[1];
      await tab.dom_cua.click({ node_id: fresh });
      let stale = '';
      try { await tab.dom_cua.click({ node_id: old }); } catch (error) { stale = String(error); }
      ({ stale, hidden: await tab.playwright.locator('button[hidden]').isVisible(), disabled: await tab.playwright.getByRole('button', { name: 'Disabled field' }).isEnabled() });
    `)
    expect(result.stale).toContain('NodeStale')
    expect(result.hidden).toBe(false)
    expect(result.disabled).toBe(false)
  })

  it('preserves cells within a turn and applies client marks only to their turn', async () => {
    const first = await evaluate('(await browser.tabs.list()).map(tab => tab.id)')
    expect(first).toHaveLength(1)
    await evaluate('await tab.markDeliverable(); await tab.markHandoff(); true')
    await manager.handleTurnNotification('turn/completed', { threadId: 'semantic', turn: { id: turnId } })
    expect(await evaluate('(await browser.tabs.list()).map(tab => tab.id)')).toEqual(first)
    await manager.handleTurnNotification('turn/completed', { threadId: 'semantic', turn: { id: turnId } })
    expect(await evaluate('(await browser.tabs.list()).map(tab => tab.id)')).toEqual(first)
    turnId = 'turn-semantic-2'
    await evaluate('await browser.tabs.list()')
    await manager.handleTurnNotification('turn/completed', { threadId: 'semantic', turn: { id: 'turn-semantic-1' } })
    expect(await evaluate('(await browser.tabs.list()).map(tab => tab.id)')).toEqual(first)
    await manager.handleTurnNotification('turn/cancelled', { threadId: 'semantic', turn: { id: turnId } })
    expect(await evaluate('await browser.tabs.list()')).toEqual([])
    turnId = 'turn-semantic-3'
    await evaluate('tab = await browser.tabs.new(); true')
    await manager.handleTurnNotification('turn/failed', { threadId: 'semantic', turn: { id: turnId } })
    expect(await evaluate('await browser.tabs.list()')).toEqual([])
  })

  it('omits documentation for capabilities that the backend does not advertise', async () => {
    const { browserDocumentation, browserTopic } = await import(new URL('../../../resources/browser/scripts/browser-documentation.mjs', import.meta.url).href)
    const info = { name: 'Limited IAB', capabilities: { browser: [], tab: [], docs: { supported: ['tabs'] } } }
    const documentation = browserDocumentation(info)
    expect(documentation).toContain('browser.tabs.new')
    expect(documentation).not.toContain('agent.documentation.get("viewport")')
    expect(documentation).not.toContain('tab.playwright.getByRole')
    expect(() => browserTopic(info, 'pageAssets')).toThrow('Available topics: tabs')
  })
})
